using System.Runtime.InteropServices;
using Ember.Core.Gpu;

namespace Ember.Native;

/// <summary>A hardware GPU from DXGI.</summary>
internal sealed record DxgiAdapter(string Name, string Luid, ulong DedicatedVideoMemory, uint VendorId)
{
    public bool Integrated => GpuAggregator.IsIntegrated(DedicatedVideoMemory);
}

/// <summary>DXGI adapter list through raw COM vtable calls (no COM interop types).</summary>
internal static unsafe class Dxgi
{
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
    private const uint VendorMicrosoft = 0x1414;

    // DXGI_ADAPTER_DESC1 (x64): Description WCHAR[128] @0, VendorId @256, DeviceId @260, SubSysId @264,
    // Revision @268, DedicatedVideoMemory @272, DedicatedSystemMemory @280, SharedSystemMemory @288,
    // AdapterLuid (LowPart @296, HighPart @300), Flags @304. Size 312.
    private const int DescSize = 312;

    public static List<DxgiAdapter> Adapters()
    {
        var list = new List<DxgiAdapter>();
        var iid = IID_IDXGIFactory1;
        if (CreateDXGIFactory1(ref iid, out var factory) != 0 || factory == IntPtr.Zero) return list;
        try
        {
            var fvt = *(IntPtr**)factory;
            // IDXGIFactory1::EnumAdapters1 = vtable slot 12
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)fvt[12];
            byte* desc = stackalloc byte[DescSize];
            for (uint i = 0; i < 16; i++)
            {
                IntPtr adapter;
                int hr = enumAdapters1(factory, i, &adapter);
                if (hr == DXGI_ERROR_NOT_FOUND || hr != 0) break;
                try
                {
                    var avt = *(IntPtr**)adapter;
                    // IDXGIAdapter1::GetDesc1 = vtable slot 10
                    var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)avt[10];
                    if (getDesc1(adapter, desc) != 0) continue;
                    string name = new string((char*)desc, 0, 128).Split('\0')[0].Trim();
                    uint vendor = *(uint*)(desc + 256);
                    ulong dedicated = *(ulong*)(desc + 272);
                    uint luidLow = *(uint*)(desc + 296);
                    int luidHigh = *(int*)(desc + 300);
                    uint flags = *(uint*)(desc + 304);
                    if ((flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0 || vendor == VendorMicrosoft) continue;
                    var luid = GpuNames.LuidText(luidHigh, luidLow);
                    if (list.Any(a => a.Luid == luid)) continue;
                    list.Add(new DxgiAdapter(name, luid, dedicated, vendor));
                }
                finally { Release(adapter); }
            }
        }
        finally { Release(factory); }
        return list;
    }

    private static void Release(IntPtr unk)
    {
        if (unk == IntPtr.Zero) return;
        var vt = *(IntPtr**)unk;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)vt[2])(unk);
    }
}
