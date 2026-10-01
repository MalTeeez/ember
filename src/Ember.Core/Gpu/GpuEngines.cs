using System.Text.RegularExpressions;

namespace Ember.Core.Gpu;

/// <summary>Parsed PDH "GPU Engine" instance name, for example
/// "pid_1234_luid_0x00000000_0x0000D1A5_phys_0_eng_3_engtype_3D".</summary>
public readonly record struct GpuEngineInstance(int Pid, string Luid, int Phys, int Engine, string EngineType)
{
    public string EngineKey => $"{Luid}_phys_{Phys}_eng_{Engine}";
}

public static partial class GpuNames
{
    [GeneratedRegex(@"^pid_(\d+)_luid_(0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_(\d+)_eng_(\d+)_engtype_(.*)$")]
    private static partial Regex EngineRegex();

    [GeneratedRegex(@"^luid_(0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_(\d+)")]
    private static partial Regex AdapterRegex();

    public static bool TryParseEngine(string instance, out GpuEngineInstance result)
    {
        var m = EngineRegex().Match(instance);
        if (!m.Success)
        {
            result = default;
            return false;
        }
        result = new GpuEngineInstance(
            int.Parse(m.Groups[1].Value),
            NormalizeLuid(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value),
            int.Parse(m.Groups[4].Value),
            m.Groups[5].Value);
        return true;
    }

    /// <summary>LUID from a "GPU Adapter Memory" instance name ("luid_0x…_0x…_phys_0").</summary>
    public static string? AdapterLuid(string instance)
    {
        var m = AdapterRegex().Match(instance);
        return m.Success ? NormalizeLuid(m.Groups[1].Value) : null;
    }

    /// <summary>LUID text from a DXGI LUID (HighPart, LowPart), same form as in PDH names.</summary>
    public static string LuidText(int high, uint low) => NormalizeLuid($"0x{(uint)high:X8}_0x{low:X8}");

    public static string NormalizeLuid(string luid) => luid.ToUpperInvariant().Replace("0X", "0x");

    private static readonly HashSet<string> NameNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "NVIDIA", "GeForce", "AMD", "ATI", "Radeon", "Intel", "R", "TM", "C", "Graphics", "RTX", "GTX", "RX", "Arc",
        "Laptop", "GPU", "Mobile", "Max", "Q", "Design", "Series", "Quadro", "Iris", "Xe", "UHD", "HD", "Pro",
    };

    /// <summary>
    /// Short label (at most 4 characters) that shows what differs between GPUs: the model number.
    /// "NVIDIA GeForce RTX 4090" → "4090", "AMD Radeon RX 7900 XTX" → "7900", "Intel(R) Arc(TM) A770" → "A770",
    /// "AMD Radeon 780M" → "780M". Without a model number: "igpu" (integrated) or "gpu".
    /// </summary>
    public static string ShortLabel(string adapterName, bool integrated)
    {
        var tokens = Regex.Split(adapterName, "[^A-Za-z0-9]+").Where(t => t.Length > 0 && !NameNoise.Contains(t)).ToList();
        var model = tokens.FirstOrDefault(t => t.Any(char.IsDigit));
        if (model != null) return model.Length <= 4 ? model : model[..4];
        return integrated ? "igpu" : "gpu";
    }

    /// <summary>Labels for several GPUs; equal labels get a number ("4090", "4092").</summary>
    public static List<string> Labels(IReadOnlyList<(string Name, bool Integrated)> gpus)
    {
        var labels = gpus.Select(g => ShortLabel(g.Name, g.Integrated)).ToList();
        foreach (var dup in labels.GroupBy(l => l).Where(g => g.Count() > 1).Select(g => g.Key).ToList())
        {
            int n = 1;
            for (int i = 0; i < labels.Count; i++)
                if (labels[i] == dup) labels[i] = dup[..Math.Min(3, dup.Length)] + n++;
        }
        return labels;
    }
}

/// <summary>Load and top process for one adapter.</summary>
public sealed record GpuAdapterLoad(string Luid, double Load, string BusiestEngineType, int TopPid, double TopPidLoad);

/// <summary>
/// Aggregates "GPU Engine(*)\Utilization Percentage" values. For each engine the
/// processes are summed; the adapter load is the busiest engine (Task Manager method).
/// The top process is the process with the highest load on any engine of the adapter.
/// </summary>
public static class GpuAggregator
{
    public static Dictionary<string, GpuAdapterLoad> Aggregate(IEnumerable<(string Instance, double Value)> samples)
    {
        var engines = new Dictionary<string, (string Luid, string Type, double Sum)>();
        var pidEngine = new Dictionary<(string Luid, int Pid, string Engine), double>();
        foreach (var (instance, value) in samples)
        {
            if (!GpuNames.TryParseEngine(instance, out var e) || double.IsNaN(value) || value < 0) continue;
            var key = e.EngineKey;
            engines.TryGetValue(key, out var cur);
            engines[key] = (e.Luid, e.EngineType, cur.Sum + value);
            var pk = (e.Luid, e.Pid, key);
            pidEngine[pk] = pidEngine.GetValueOrDefault(pk) + value;
        }

        var result = new Dictionary<string, GpuAdapterLoad>();
        foreach (var group in engines.Values.GroupBy(v => v.Luid))
        {
            var busiest = group.OrderByDescending(g => g.Sum).First();
            int topPid = 0;
            double topLoad = 0;
            foreach (var kv in pidEngine)
            {
                if (kv.Key.Luid != group.Key || kv.Key.Pid == 0) continue;
                if (kv.Value > topLoad)
                {
                    topLoad = kv.Value;
                    topPid = kv.Key.Pid;
                }
            }
            result[group.Key] = new GpuAdapterLoad(group.Key, Math.Min(100, busiest.Sum), busiest.Type, topPid, Math.Min(100, topLoad));
        }
        return result;
    }

    /// <summary>Integrated GPUs use shared memory: less than 512 MiB dedicated memory.</summary>
    public static bool IsIntegrated(ulong dedicatedVideoMemory) => dedicatedVideoMemory < 512UL * 1024 * 1024;
}
