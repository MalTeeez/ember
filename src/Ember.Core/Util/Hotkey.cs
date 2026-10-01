namespace Ember.Core.Util;

/// <summary>Parses hotkey text such as "Ctrl+Alt+N" into RegisterHotKey modifiers and a virtual-key code.</summary>
public static class Hotkey
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;

    public static bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false; // need at least one modifier
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win": case "windows": modifiers |= ModWin; break;
                default: modifiers = 0; return false;
            }
        }
        if (!TryKey(parts[^1], out vk)) { modifiers = 0; return false; }
        return true;
    }

    private static bool TryKey(string key, out uint vk)
    {
        vk = 0;
        key = key.ToUpperInvariant();
        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
            return false;
        }
        if (key[0] == 'F' && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1);
            return true;
        }
        switch (key)
        {
            case "SPACE": vk = 0x20; return true;
            case "ENTER": case "RETURN": vk = 0x0D; return true;
            case "INSERT": case "INS": vk = 0x2D; return true;
            case "HOME": vk = 0x24; return true;
            case "END": vk = 0x23; return true;
            case "PAGEUP": vk = 0x21; return true;
            case "PAGEDOWN": vk = 0x22; return true;
        }
        return false;
    }
}
