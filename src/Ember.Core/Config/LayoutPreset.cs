using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core.Config;

/// <summary>
/// One row of the widget layout. Kind "item" = an item that makes 0..n lines; "header" = a section
/// title; "spacer" = a fixed line (blank, rule, dots, dashed, double or custom text).
/// Headers and spacers with <see cref="Collapse"/> show only while the items after them have lines.
/// </summary>
public sealed class LayoutEntry
{
    public string Kind { get; set; } = "item";
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Header title, or spacer text (custom spacer).</summary>
    public string? Text { get; set; }
    /// <summary>Spacer preset: blank, rule, dots, dashed, double, custom.</summary>
    public string? Preset { get; set; }
    public bool Collapse { get; set; } = true;

    public static LayoutEntry Item(string id, bool enabled = true) => new() { Kind = "item", Id = id, Enabled = enabled };
    public static LayoutEntry Header(string text) => new() { Kind = "header", Id = "header", Text = text };
    public static LayoutEntry Spacer(string preset = "blank", string? text = null, bool collapse = true)
        => new() { Kind = "spacer", Id = "spacer", Preset = preset, Text = text, Collapse = collapse };

    public LayoutEntry Clone() => (LayoutEntry)MemberwiseClone();

    /// <summary>Text for the settings list.</summary>
    public string Describe() => Kind switch
    {
        "header" => $"── header: {Text}",
        "spacer" => Preset == "custom" ? $"·· spacer: \"{Text}\"" : $"·· spacer ({Preset ?? "blank"})",
        _ => ItemCatalog.Title(Id),
    };
}

/// <summary>All items that the layout can contain.</summary>
public static class ItemCatalog
{
    public sealed record ItemInfo(string Id, string Title, string Group);

    public static readonly IReadOnlyList<ItemInfo> All = new ItemInfo[]
    {
        new("toolbar", "Toolbar (new note, minimized notes, close)", "widget"),
        new("cpu.load", "CPU load", "cpu"),
        new("cpu.cores", "CPU hot cores (load, clock, temperature)", "cpu"),
        new("cpu.throttle", "CPU throttling", "cpu"),
        new("cpu.top", "CPU top process", "cpu"),
        new("net.down", "Network down", "network"),
        new("net.up", "Network up", "network"),
        new("net.adapters", "Network adapter names", "network"),
        new("net.quality", "Network quality (ping)", "network"),
        new("gpu.load", "GPU load and temperature", "gpu"),
        new("gpu.vram", "GPU VRAM", "gpu"),
        new("gpu.top", "GPU top process", "gpu"),
        new("mem.avail", "Available RAM", "memory"),
        new("mem.commit", "Commit charge", "memory"),
        new("mem.pagein", "Hard page faults", "memory"),
        new("mem.top", "Top memory process", "memory"),
        new("disk.busy", "Disk busy", "disk"),
        new("disk.free", "Disk free space", "disk"),
        new("disk.top", "Top disk process", "disk"),
        new("claude.5h", "Claude 5h limit", "claude"),
        new("claude.week", "Claude weekly limit", "claude"),
        new("claude.models", "Claude per-model weekly limits", "claude"),
        new("codex.5h", "Codex 5h limit", "codex"),
        new("codex.week", "Codex weekly limit", "codex"),
        new("copilot.premium", "Copilot premium requests (monthly)", "copilot"),
        new("copilot.other", "Copilot chat / completions quotas (Free plan)", "copilot"),
        new("devices.battery", "Device batteries (Bluetooth, Razer wireless)", "devices"),
    };

    public static string Title(string id) => All.FirstOrDefault(i => i.Id == id)?.Title ?? id;
    public static bool Exists(string id) => All.Any(i => i.Id == id);
}

/// <summary>A named layout (list of entries). Exported and imported as JSON.</summary>
public sealed class LayoutPreset
{
    public string Name { get; set; } = "default";
    public List<LayoutEntry> Entries { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Null if not valid. Unknown items are removed; missing items are added at the end, disabled.</summary>
    public static LayoutPreset? Parse(string json)
    {
        try
        {
            var p = JsonSerializer.Deserialize<LayoutPreset>(json, Options);
            if (p?.Entries == null) return null;
            p.Entries = Normalize(p.Entries);
            return p;
        }
        catch (JsonException) { return null; }
    }

    /// <param name="addMissingAsDefault">
    /// false (import): missing items are added at the end, switched off.
    /// true (settings of an older version): missing items are added like in the default layout
    /// (on/off as there, with their section header), so new features show.
    /// </param>
    public static List<LayoutEntry> Normalize(List<LayoutEntry>? entries, bool addMissingAsDefault = false)
    {
        var list = new List<LayoutEntry>();
        var seen = new HashSet<string>();
        foreach (var e in entries ?? new())
        {
            if (e == null) continue;
            e.Kind = e.Kind?.ToLowerInvariant() switch { "header" => "header", "spacer" => "spacer", _ => "item" };
            if (e.Kind == "item")
            {
                if (!ItemCatalog.Exists(e.Id) || !seen.Add(e.Id)) continue;
            }
            else e.Id = e.Kind;
            list.Add(e);
        }
        var missing = ItemCatalog.All.Where(i => !seen.Contains(i.Id)).ToList();
        if (!addMissingAsDefault)
        {
            foreach (var i in missing) list.Add(LayoutEntry.Item(i.Id, enabled: false));
            return list;
        }
        var defaults = Default().Entries;
        foreach (var group in missing.GroupBy(i => i.Group))
        {
            bool newGroup = !ItemCatalog.All.Any(i => i.Group == group.Key && seen.Contains(i.Id));
            if (newGroup && group.Key is not ("cpu" or "widget"))
            {
                list.Add(LayoutEntry.Spacer());
                list.Add(LayoutEntry.Header(group.Key));
            }
            foreach (var i in group)
                list.Add(LayoutEntry.Item(i.Id, defaults.FirstOrDefault(d => d.Id == i.Id)?.Enabled ?? false));
        }
        return list;
    }

    /// <summary>Version 2: the network items have a "net" section header.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Changes an older layout to the current version (adds new section headers).</summary>
    public static void Migrate(List<LayoutEntry> entries, int fromVersion)
    {
        if (fromVersion < 2)
        {
            int first = entries.FindIndex(e => e.Kind == "item" && e.Id.StartsWith("net.", StringComparison.Ordinal));
            if (first < 0) return;
            int k = first - 1;
            while (k >= 0 && entries[k].Kind == "spacer") k--;
            if (k >= 0 && entries[k].Kind == "header") return; // already has a header
            entries.Insert(first, LayoutEntry.Header("net"));
            if (first > 0) entries.Insert(first, LayoutEntry.Spacer());
        }
    }

    public static IReadOnlyList<LayoutPreset> BuiltIn { get; } = new[] { Default(), Compact(), Everything() };

    public static LayoutPreset? Find(string name) => BuiltIn.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } p
        ? Parse(p.ToJson())
        : null;

    /// <summary>The spec layout: CPU without header; network, GPU, memory, disk, Claude, Codex, Copilot and device sections.</summary>
    public static LayoutPreset Default() => new()
    {
        Name = "default",
        Entries = new()
        {
            LayoutEntry.Item("toolbar"),
            LayoutEntry.Item("cpu.load"), LayoutEntry.Item("cpu.cores"), LayoutEntry.Item("cpu.throttle"), LayoutEntry.Item("cpu.top"),
            LayoutEntry.Spacer(), LayoutEntry.Header("net"),
            LayoutEntry.Item("net.down"), LayoutEntry.Item("net.up"), LayoutEntry.Item("net.adapters"), LayoutEntry.Item("net.quality", false),
            LayoutEntry.Spacer(), LayoutEntry.Header("gpu"),
            LayoutEntry.Item("gpu.load"), LayoutEntry.Item("gpu.vram", false), LayoutEntry.Item("gpu.top"),
            LayoutEntry.Spacer(), LayoutEntry.Header("memory"),
            LayoutEntry.Item("mem.avail"), LayoutEntry.Item("mem.commit"), LayoutEntry.Item("mem.pagein"), LayoutEntry.Item("mem.top"),
            LayoutEntry.Spacer(), LayoutEntry.Header("disk"),
            LayoutEntry.Item("disk.busy"), LayoutEntry.Item("disk.free"), LayoutEntry.Item("disk.top"),
            LayoutEntry.Spacer(), LayoutEntry.Header("claude"),
            LayoutEntry.Item("claude.5h"), LayoutEntry.Item("claude.week"), LayoutEntry.Item("claude.models"),
            LayoutEntry.Spacer(), LayoutEntry.Header("codex"),
            LayoutEntry.Item("codex.5h"), LayoutEntry.Item("codex.week"),
            LayoutEntry.Spacer(), LayoutEntry.Header("copilot"),
            LayoutEntry.Item("copilot.premium"), LayoutEntry.Item("copilot.other"),
            LayoutEntry.Spacer(), LayoutEntry.Header("devices"),
            LayoutEntry.Item("devices.battery"),
        },
    };

    /// <summary>Fewer lines: no toolbar, no headers, Claude first.</summary>
    public static LayoutPreset Compact() => new()
    {
        Name = "compact",
        Entries = Normalize(new()
        {
            LayoutEntry.Item("claude.5h"), LayoutEntry.Item("claude.week"),
            LayoutEntry.Spacer("rule"),
            LayoutEntry.Item("cpu.load"), LayoutEntry.Item("cpu.top"),
            LayoutEntry.Item("net.down"), LayoutEntry.Item("net.up"),
            LayoutEntry.Item("gpu.load"), LayoutEntry.Item("gpu.top"),
            LayoutEntry.Item("mem.avail"), LayoutEntry.Item("mem.top"),
            LayoutEntry.Item("disk.free"),
        }),
    };

    /// <summary>Every item on, with dotted spacers.</summary>
    public static LayoutPreset Everything() => new()
    {
        Name = "everything",
        Entries = Default().Entries.Select(e =>
        {
            var c = e.Clone();
            c.Enabled = true;
            if (c.Kind == "spacer") c.Preset = "dots";
            return c;
        }).ToList(),
    };
}
