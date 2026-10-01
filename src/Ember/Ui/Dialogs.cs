using System.Runtime.InteropServices;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>Common dialogs: colour picker and JSON open/save.</summary>
internal static class Dialogs
{
    private static readonly IntPtr CustomColors = Marshal.AllocHGlobal(16 * 4);
    private static bool _customInit;

    public static Rgb? PickColor(IntPtr owner, Rgb current)
    {
        if (!_customInit)
        {
            for (int i = 0; i < 16; i++) Marshal.WriteInt32(CustomColors, i * 4, 0x00FFFFFF);
            _customInit = true;
        }
        var cc = new CHOOSECOLOR
        {
            lStructSize = (uint)Marshal.SizeOf<CHOOSECOLOR>(),
            hwndOwner = owner,
            rgbResult = current.ToColorRef(),
            lpCustColors = CustomColors,
            Flags = W.CC_RGBINIT | W.CC_FULLOPEN,
        };
        return W.ChooseColor(ref cc) ? Rgb.FromColorRef(cc.rgbResult) : null;
    }

    public static string? OpenJson(IntPtr owner, string title) => FileDialog(owner, title, null, save: false);

    public static string? SaveJson(IntPtr owner, string title, string defaultName) => FileDialog(owner, title, defaultName, save: true);

    private static string? FileDialog(IntPtr owner, string title, string? defaultName, bool save)
    {
        const int max = 1024;
        IntPtr file = Marshal.AllocHGlobal(max * 2);
        IntPtr filter = Marshal.StringToHGlobalUni("JSON files (*.json)\0*.json\0All files\0*.*\0\0");
        IntPtr titlePtr = Marshal.StringToHGlobalUni(title);
        IntPtr ext = Marshal.StringToHGlobalUni("json");
        try
        {
            var name = (defaultName ?? "") + "\0";
            var chars = name.ToCharArray();
            Marshal.Copy(chars, 0, file, Math.Min(chars.Length, max - 1));
            Marshal.WriteInt16(file, (max - 1) * 2, 0);
            var ofn = new OPENFILENAME
            {
                lStructSize = (uint)Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = owner,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = file,
                nMaxFile = max,
                lpstrTitle = titlePtr,
                lpstrDefExt = ext,
                Flags = W.OFN_NOCHANGEDIR | W.OFN_PATHMUSTEXIST | (save ? W.OFN_OVERWRITEPROMPT : W.OFN_FILEMUSTEXIST),
            };
            bool ok = save ? W.GetSaveFileName(ref ofn) : W.GetOpenFileName(ref ofn);
            return ok ? Marshal.PtrToStringUni(file) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(file);
            Marshal.FreeHGlobal(filter);
            Marshal.FreeHGlobal(titlePtr);
            Marshal.FreeHGlobal(ext);
        }
    }
}
