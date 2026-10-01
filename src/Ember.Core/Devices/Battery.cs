namespace Ember.Core.Devices;

/// <summary>Battery of a connected device (Bluetooth or a Razer 2.4 GHz receiver).</summary>
public sealed record DeviceBattery(string Name, double Percent, bool? Charging, string Source);

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
              .Select(g => g.OrderBy(b => b.Percent).First() with { Name = g.Key })
              .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
