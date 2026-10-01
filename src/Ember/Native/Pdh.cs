using System.Runtime.InteropServices;

namespace Ember.Native;

/// <summary>PDH (performance counters) query. Counter paths are English (PdhAddEnglishCounter).</summary>
internal sealed class PdhQuery : IDisposable
{
    private IntPtr _query;
    private readonly List<PdhCounter> _counters = new();

    private const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhRemoveCounter(IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    public PdhQuery()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) _query = IntPtr.Zero;
    }

    public bool IsOpen => _query != IntPtr.Zero;

    /// <summary>Adds a counter. Null if the path is not valid on this machine.</summary>
    public PdhCounter? Add(string path)
    {
        if (_query == IntPtr.Zero) return null;
        if (PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var h) != 0) return null;
        var c = new PdhCounter(h, path);
        _counters.Add(c);
        return c;
    }

    public void Remove(PdhCounter? c)
    {
        if (c == null) return;
        PdhRemoveCounter(c.Handle);
        _counters.Remove(c);
    }

    public bool Collect() => _query != IntPtr.Zero && PdhCollectQueryData(_query) == 0;

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
        _counters.Clear();
    }

    internal sealed class PdhCounter
    {
        public IntPtr Handle { get; }
        public string Path { get; }
        private IntPtr _buffer;
        private uint _bufferSize;

        public PdhCounter(IntPtr h, string path) { Handle = h; Path = path; }

        /// <summary>Single value, or null if not valid (for example the first sample of a rate counter).</summary>
        public double? Value()
        {
            if (PdhGetFormattedCounterValue(Handle, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var v) != 0) return null;
            if (v.CStatus > 1) return null; // PDH_CSTATUS_VALID_DATA (0) or NEW_DATA (1)
            return v.DoubleValue;
        }

        /// <summary>All instances of a wildcard counter, or null on failure.</summary>
        public List<(string Instance, double Value)>? Array()
        {
            uint size = 0, count = 0;
            uint st = PdhGetFormattedCounterArray(Handle, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, ref count, IntPtr.Zero);
            if (st != PDH_MORE_DATA) return st == 0 ? new() : null;
            if (size > _bufferSize)
            {
                if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
                _bufferSize = size * 2;
                _buffer = Marshal.AllocHGlobal((int)_bufferSize);
            }
            size = _bufferSize;
            st = PdhGetFormattedCounterArray(Handle, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, ref count, _buffer);
            if (st != 0) return null;
            var list = new List<(string, double)>((int)count);
            // PDH_FMT_COUNTERVALUE_ITEM_W: LPWSTR szName (8) + PDH_FMT_COUNTERVALUE (16) = 24 bytes.
            for (int i = 0; i < count; i++)
            {
                IntPtr item = _buffer + i * 24;
                string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                uint status = (uint)Marshal.ReadInt32(item + 8);
                if (status > 1) continue;
                double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + 16));
                list.Add((name, value));
            }
            return list;
        }
    }
}
