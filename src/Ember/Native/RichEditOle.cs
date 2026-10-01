using System.Runtime.InteropServices;

namespace Ember.Native;

/// <summary>
/// ITextDocument of a RichEdit control (TOM), through raw vtable calls.
/// Used to keep formatting changes out of the undo stack (Undo(tomSuspend) / Undo(tomResume)).
/// </summary>
internal static unsafe class RichEditOle
{
    private const uint EM_GETOLEINTERFACE = W.WM_USER + 60;
    private static readonly Guid IID_ITextDocument = new("8CC497C0-A1DF-11CE-8098-00AA0047BE5D");
    private const int tomSuspend = -9999995, tomResume = -9999994;
    private const int SlotUndo = 22; // IUnknown 3 + IDispatch 4 + ITextDocument methods up to Undo

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessagePtr(IntPtr hwnd, uint msg, IntPtr wParam, out IntPtr lParam);

    /// <summary>ITextDocument pointer (AddRef'ed) or zero.</summary>
    public static IntPtr TextDocument(IntPtr edit)
    {
        try
        {
            if (SendMessagePtr(edit, EM_GETOLEINTERFACE, IntPtr.Zero, out var ole) == IntPtr.Zero || ole == IntPtr.Zero) return IntPtr.Zero;
            var iid = IID_ITextDocument;
            int hr = Marshal.QueryInterface(ole, in iid, out var doc);
            Marshal.Release(ole);
            return hr == 0 ? doc : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
    }

    private static void Undo(IntPtr doc, int count)
    {
        if (doc == IntPtr.Zero) return;
        var vt = *(IntPtr**)doc;
        int result;
        ((delegate* unmanaged[Stdcall]<IntPtr, int, int*, int>)vt[SlotUndo])(doc, count, &result);
    }

    public static void SuspendUndo(IntPtr doc) => Undo(doc, tomSuspend);
    public static void ResumeUndo(IntPtr doc) => Undo(doc, tomResume);

    public static void Release(IntPtr doc)
    {
        if (doc != IntPtr.Zero) Marshal.Release(doc);
    }
}
