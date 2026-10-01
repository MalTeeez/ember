namespace Ember.Core.Devices;

/// <summary>Battery of a connected device (Bluetooth or a Razer 2.4 GHz receiver).</summary>
public sealed record DeviceBattery(string Name, double Percent, bool? Charging, string Source)
{
    /// <summary>Maker ("Razer", "Sony"), from the full name or the source; empty if unknown.</summary>
    public string Brand { get; init; } = "";
}

/// <summary>
/// Razer HID protocol (as used by Razer Synapse and OpenRazer): a 90-byte feature report.
/// Layout: 0 status, 1 transaction id, 2-3 remaining packets, 4 protocol type, 5 data size,
/// 6 command class, 7 command id, 8-87 arguments, 88 CRC (XOR of bytes 2..87), 89 reserved.
/// Battery: class 0x07, id 0x80, data size 2, level in argument 1 (0..255). Charging: class 0x07, id 0x84.
/// </summary>
public static class RazerProtocol
{
    public const int ReportLength = 90;
    public const ushort VendorId = 0x1532;
    public const byte StatusNew = 0x00, StatusBusy = 0x01, StatusSuccess = 0x02, StatusFailure = 0x03, StatusTimeout = 0x04, StatusNotSupported = 0x05;
    public const byte ClassPower = 0x07, CmdBattery = 0x80, CmdCharging = 0x84;

    /// <summary>Transaction ids to try: newer wireless devices use 0x1F, others 0xFF or 0x3F.</summary>
    public static readonly byte[] TransactionIds = { 0x1F, 0xFF, 0x3F };

    /// <summary>Known wireless receivers (product ids). Other Razer receivers are found by their product name.</summary>
    public static readonly IReadOnlyDictionary<ushort, string> KnownReceivers = new Dictionary<ushort, string>
    {
        [0x00C5] = "DeathAdder V3 HyperSpeed",
    };

    public static byte[] Request(byte transactionId, byte commandClass, byte commandId, byte dataSize)
    {
        var r = new byte[ReportLength];
        r[0] = StatusNew;
        r[1] = transactionId;
        r[5] = dataSize;
        r[6] = commandClass;
        r[7] = commandId;
        r[88] = Crc(r);
        return r;
    }

    public static byte Crc(ReadOnlySpan<byte> report)
    {
        byte crc = 0;
        for (int i = 2; i < 88; i++) crc ^= report[i];
        return crc;
    }

    /// <summary>Battery % from a response to <see cref="CmdBattery"/>, or null (wrong command, not successful, bad CRC).</summary>
    public static double? BatteryPercent(ReadOnlySpan<byte> response)
    {
        if (!IsSuccess(response, ClassPower, CmdBattery)) return null;
        return Math.Round(response[9] * 100.0 / 255.0, 1);
    }

    /// <summary>Charging state from a response to <see cref="CmdCharging"/>, or null.</summary>
    public static bool? Charging(ReadOnlySpan<byte> response)
        => IsSuccess(response, ClassPower, CmdCharging) ? response[9] != 0 : null;

    public static bool IsSuccess(ReadOnlySpan<byte> response, byte commandClass, byte commandId)
        => response.Length >= ReportLength && response[0] == StatusSuccess && response[6] == commandClass && response[7] == commandId
           && response[88] == Crc(response);

    /// <summary>A Razer HID device that may be a wireless receiver: known product id, or a name with HyperSpeed/Wireless/Receiver/Dongle.</summary>
    public static bool IsReceiver(ushort productId, string? productName)
    {
        if (KnownReceivers.ContainsKey(productId)) return true;
        if (string.IsNullOrEmpty(productName)) return false;
        foreach (var w in new[] { "HyperSpeed", "Wireless", "Receiver", "Dongle" })
            if (productName.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

public static class DeviceNames
{
    /// <summary>Shorter display name: drops vendor and noise words ("Razer DeathAdder V3 HyperSpeed" → "DeathAdder V3 HyperSpeed").</summary>
    public static string Short(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.Equals("Razer", StringComparison.OrdinalIgnoreCase) && !w.Equals("Hands-Free", StringComparison.OrdinalIgnoreCase)
                        && !w.Equals("AG", StringComparison.OrdinalIgnoreCase) && !w.Equals("Audio", StringComparison.OrdinalIgnoreCase)
                        && !w.Equals("Avrcp", StringComparison.OrdinalIgnoreCase) && !w.Equals("Transport", StringComparison.OrdinalIgnoreCase));
        var s = string.Join(' ', words).Trim();
        return s.Length == 0 ? name.Trim() : s;
    }

    /// <summary>One entry per device name: the lowest battery wins (a headset can report through several services).</summary>
    public static List<DeviceBattery> Merge(IEnumerable<DeviceBattery> all)
        => all.GroupBy(b => Short(b.Name), StringComparer.OrdinalIgnoreCase)
              .Select(g =>
              {
                  var b = g.OrderBy(x => x.Percent).First();
                  return b with { Name = g.Key, Brand = b.Brand.Length > 0 ? b.Brand : Brand(b.Name, b.Source) };
              })
              .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static readonly string[] Brands =
    {
        "Razer", "Logitech", "Sony", "Apple", "Samsung", "Bose", "JBL", "SteelSeries", "Corsair", "HyperX", "Microsoft", "Xbox",
        "Jabra", "Sennheiser", "Beats", "Google", "Nothing", "Anker", "Soundcore", "Asus", "ROG", "Glorious", "Keychron", "Nintendo", "8BitDo",
    };

    /// <summary>Maker from a known brand word in the name, or from the source (Razer receivers).</summary>
    public static string Brand(string name, string source)
    {
        foreach (var w in name.Split(' ', '-', '(', ')'))
        {
            var b = Brands.FirstOrDefault(x => x.Equals(w, StringComparison.OrdinalIgnoreCase));
            if (b != null) return b;
        }
        if (name.StartsWith("WH-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("WF-", StringComparison.OrdinalIgnoreCase)) return "Sony";
        if (name.StartsWith("AirPods", StringComparison.OrdinalIgnoreCase)) return "Apple";
        if (name.StartsWith("Galaxy", StringComparison.OrdinalIgnoreCase)) return "Samsung";
        if (name.StartsWith("MX ", StringComparison.OrdinalIgnoreCase) || name.StartsWith("G", StringComparison.Ordinal) && name.Length > 1 && char.IsDigit(name[1])) return "Logitech";
        return source == "razer" ? "Razer" : "";
    }

    /// <summary>Kind of device from its name: mouse, keyboard, headset, earbuds, controller, speaker, pen, device.</summary>
    public static string Kind(string name)
    {
        string n = name.ToLowerInvariant();
        bool Any(params string[] words) => words.Any(w => n.Contains(w, StringComparison.Ordinal));
        if (Any("mouse", "deathadder", "viper", "basilisk", "naga", "orochi", "pro click", "mx master", "mx anywhere", "g502", "g305", "g703", "g903", "superlight", "rival", "aerox", "prime", "model o", "model d", "dark core", "harpoon", "ironclaw")) return "mouse";
        if (Any("keyboard", "keys", "blackwidow", "huntsman", "ornata", "k70", "k95", "k100", "apex", "mx mechanical", "keychron")) return "keyboard";
        if (Any("buds", "airpods", "wf-", "earbuds", "pods")) return "earbuds";
        if (Any("headset", "headphone", "wh-", "kraken", "blackshark", "barracuda", "nari", "arctis", "cloud", "quietcomfort", "qc", "momentum", "g pro x", "g733", "virtuoso", "void")) return "headset";
        if (Any("controller", "gamepad", "dualsense", "dualshock", "xbox", "joy-con", "pro controller", "wolverine")) return "controller";
        if (Any("speaker", "flip", "charge", "boom", "soundlink")) return "speaker";
        if (Any("pen", "stylus", "pencil")) return "pen";
        return "device";
    }

    /// <summary>"Razer Mouse" (brand and kind) for names that are too long; a number when several are the same.</summary>
    public static List<string> Display(IReadOnlyList<DeviceBattery> devices, int width)
    {
        var names = devices.Select(d =>
        {
            if (d.Name.Length <= width) return d.Name;
            string kind = Kind(d.Name);
            kind = char.ToUpperInvariant(kind[0]) + kind[1..]; // "Razer Mouse"
            string brand = d.Brand.Length > 0 ? d.Brand : Brand(d.Name, d.Source);
            return brand.Length > 0 ? brand + " " + kind : kind;
        }).ToList();
        foreach (var dup in names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList())
        {
            int k = 1;
            for (int i = 0; i < names.Count; i++)
                if (names[i] == dup) names[i] = dup + " " + k++;
        }
        return names;
    }
}
