using Ember.Core.Claude;
using Ember.Core.Config;
using Ember.Core.Gpu;
using Ember.Core.Net;
using Ember.Core.Render;
using Ember.Core.Stats;

namespace Ember.Core.Status;

/// <summary>Things outside the snapshot that the widget shows (toolbar).</summary>
public sealed class ComposeContext
{
    /// <summary>Minimized notes: id and title (for the toolbar buttons).</summary>
    public IReadOnlyList<(string Id, string Title)> MinimizedNotes { get; init; } = Array.Empty<(string, string)>();
}

/// <summary>
/// Turns snapshots into widget lines. All items are composed first (fixed order, so their
/// state does not depend on the layout), then placed in the order of the layout entries.
/// Keeps graph history, smoothed core loads, show/hide state, kept heights and sticky top processes.
/// Pure: no Windows APIs.
/// </summary>
public sealed class StatusComposer
{
    private readonly VisibilitySet _vis = new();
    private readonly CoreSmoother _cores = new();
    private readonly Dictionary<string, History> _hist = new();
    private readonly HashSet<string> _usedHist = new();
    private readonly HeightKeeper _keep = new();
    private readonly Dictionary<string, StickyTop> _sticky = new();
    private ThemeColors _c = ThemeColors.From(new Theme());
    private Theme? _themeRef;
    private EmberSettings _cfg = new();
    private bool _preview;
    private DateTime _now;
    private TimeSpan _stick;
    public Needs Needs { get; } = new();

    private const int HistorySamples = Layout.GraphChars * 2;
    private const double AdapterTrafficBps = 8_000; // 1 KB/s

    public ThemeColors Colors => _c;

    private History Hist(string key)
    {
        _usedHist.Add(key);
        if (!_hist.TryGetValue(key, out var h)) _hist[key] = h = new History(HistorySamples);
        return h;
    }

    /// <summary>Clear all state (after a pause, so old samples do not show as a gap).</summary>
    public void Reset()
    {
        _hist.Clear();
        _cores.Reset();
        _keep.Clear();
        foreach (var s in _sticky.Values) s.Reset();
    }

    private bool On(string id) => _cfg.IsOn(id);

    /// <summary>Show/hide state; in preview mode every condition is true at once.</summary>
    private bool Vis(string key, bool cond) => _preview || _vis.Update(key, cond, _now);

    private ProcInfo? Sticky(string key, IReadOnlyList<ProcInfo>? candidates)
    {
        if (!_sticky.TryGetValue(key, out var st)) _sticky[key] = st = new StickyTop();
        return st.Update(candidates, _now, _stick);
    }

    public List<Line> Compose(Snapshot s, EmberSettings cfg, ComposeContext? ctx = null)
    {
        _cfg = cfg;
        _now = s.Time;
        _preview = cfg.PreviewAll;
        _stick = TimeSpan.FromSeconds(cfg.Layout.ProcessStickSeconds);
        if (!ReferenceEquals(_themeRef, cfg.Theme))
        {
            _themeRef = cfg.Theme;
            _c = ThemeColors.From(cfg.Theme);
        }
        if (_preview) s = DemoData.Fill(s);

        _vis.ShowDelay = TimeSpan.FromSeconds(cfg.Intervals.ShowDelaySeconds);
        _vis.HideDelay = TimeSpan.FromSeconds(cfg.Intervals.HideDelaySeconds);
        _vis.BeginTick();
        _usedHist.Clear();

        var items = new Dictionary<string, List<Line>>();
        if (On("toolbar")) items["toolbar"] = new() { Toolbar(ctx ?? new ComposeContext()) };
        ComposeCpu(s, items);
        ComposeNet(s, items);
        ComposeGpu(s, items);
        ComposeMemory(s, items);
        ComposeDisk(s, items);
        ComposeClaude(s, items);

        _vis.EndTick();
        foreach (var k in _hist.Keys.Where(k => !_usedHist.Contains(k)).ToList()) _hist.Remove(k);

        return Arrange(items);
    }

    // ───────────── Layout ─────────────

    /// <summary>Places item lines in layout order, keeps heights, and shows headers/spacers of non-empty groups.</summary>
    private List<Line> Arrange(Dictionary<string, List<Line>> items)
    {
        var entries = _cfg.Entries;
        var hold = _preview ? TimeSpan.Zero : TimeSpan.FromSeconds(_cfg.Layout.HoldSeconds);
        var height = new int[entries.Count];
        var output = new List<Line>?[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Kind != "item" || !e.Enabled) continue;
            var lines = items.GetValueOrDefault(e.Id) ?? new List<Line>();
            int h = _keep.Reserve(e.Id, lines.Count, _now, hold);
            var list = new List<Line>(lines);
            while (list.Count < h) list.Add(Layout.Blank(padding: true));
            height[i] = h;
            output[i] = list;
        }

        var result = new List<Line>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (!e.Enabled) continue;
            if (e.Kind == "item")
            {
                result.AddRange(output[i]!);
                continue;
            }
            if (e.Collapse && GroupHeight(entries, height, i) == 0) continue;
            result.Add(e.Kind == "header" ? Layout.Header(e.Text ?? "", _c) : Layout.Spacer(e.Preset, e.Text, _c));
        }
        // No empty lines at the top or bottom of the widget.
        while (result.Count > 0 && IsEmpty(result[0]) && !result[0].IsPadding) result.RemoveAt(0);
        while (result.Count > 0 && IsEmpty(result[^1]) && !result[^1].IsPadding) result.RemoveAt(result.Count - 1);
        // The top row moves the widget (drag); its buttons still work (smaller hits win).
        if (result.Count > 0 && !result[0].Hits.Any(h => h.Width >= Layout.Width))
            result[0].Hits.Insert(0, new Hit(0, Layout.Width, HitAction.Drag, "", ""));
        return result;
    }

    private static bool IsEmpty(Line l) => l.Text.Trim().Length == 0 && l.Hits.Count == 0;

    /// <summary>Kept height of the items after a header/spacer (skipping headers/spacers, up to the next one after an item).</summary>
    private static int GroupHeight(List<LayoutEntry> entries, int[] height, int index)
    {
        int k = index + 1;
        while (k < entries.Count && entries[k].Kind != "item") k++;
        int sum = 0;
        while (k < entries.Count && entries[k].Kind == "item")
        {
            sum += height[k];
            k++;
        }
        return sum;
    }

    // ───────────── Helpers ─────────────

    private RunStyle ValueStyle => _c.Theme.BoldValues ? RunStyle.Bold : RunStyle.None;

    private LineBuilder Label(string label, Rgb accent)
    {
        var b = new LineBuilder();
        b.Add(label.Length >= Layout.LabelWidth ? label[..(Layout.LabelWidth - 1)] : label, accent);
        return b.PadTo(Layout.LabelWidth);
    }

    private LineBuilder Val(LineBuilder b, string text, Rgb? color = null) => b.Add(text, color ?? _c.Text, ValueStyle);

    /// <summary>Braille graph at the graph column; each cell coloured by its value (intensity), the accent, or mono.</summary>
    private void Graph(LineBuilder b, IReadOnlyList<double> values, double scale, Rgb accent)
    {
        b.PadTo(Layout.GraphCol);
        var (text, max) = Glyphs.Graph(values, Layout.GraphChars, scale);
        for (int i = 0; i < text.Length; i++)
            b.Add(text[i].ToString(), double.IsNaN(max[i]) ? _c.Dim : _c.GraphCell(scale > 0 ? max[i] / scale : 0, accent));
    }

    /// <summary>"  └ name.exe        58% ⓘ ⚙ ✕" — click = details; icons = details, file properties, end process.</summary>
    private Line ProcessLine(ProcInfo p)
    {
        string pid = p.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var b = new LineBuilder();
        b.LineHit(HitAction.ProcessDetails, pid, $"{p.Name} (PID {pid}) — details");
        string value = p.ValueText.Length > 6 ? p.ValueText[..6] : p.ValueText.PadLeft(4);
        int nameWidth = Layout.Width - 4 - value.Length - 1 - 7;
        b.Add("  └ ", _c.Dim).Add(Format.Fit(p.Name, nameWidth), _c.Process).Add(" ", _c.Dim);
        Val(b, value);
        b.Add(" ", _c.Dim);
        b.Button(Icons.Info, _c.Icon, HitAction.ProcessDetails, pid, "Details");
        b.Button(Icons.OpenFile, _c.Icon, HitAction.ProcessProperties, pid, "File properties");
        b.Button(Icons.Cancel, _c.Crit, HitAction.ProcessKill, pid, "End process");
        return b.Build();
    }

    private Line Toolbar(ComposeContext ctx)
    {
        var b = new LineBuilder();
        b.Button(Icons.Add, _c.Icon, HitAction.NewNote, "", "New note");
        foreach (var (id, title) in ctx.MinimizedNotes.Take(10))
        {
            b.Add(" ", _c.Dim);
            b.Button(Icons.Note, _c.Label, HitAction.RestoreNote, id, string.IsNullOrWhiteSpace(title) ? "Note" : title);
        }
        b.PadTo(Layout.Width - 2);
        b.Button(Icons.Close, _c.Icon, HitAction.HideWidget, "", "Hide widget (tray icon shows it again)");
        return b.Build();
    }

    // ───────────── CPU ─────────────

    private void ComposeCpu(Snapshot s, Dictionary<string, List<Line>> items)
    {
        var t = _cfg.Thresholds;
        var f = _cfg.Fields;
        Needs.TopCpu = false;
        if (s.Cpu is not { } cpu) { _cores.Reset(); return; }

        double load = Math.Clamp(cpu.Load, 0, 100);
        var h = Hist("cpu");
        h.Add(load);
        bool tempVis = f.CpuTemp && cpu.Temp is { } temp0 && Vis("cpu.temp", t.CpuTemp.Crossed(temp0));

        if (On("cpu.load"))
        {
            var b = Label("cpu", _c.Cpu);
            Val(b, Format.Pct4(load));
            b.PadTo(Layout.F2);
            if (f.CpuClock && cpu.ClockMHz is { } clk) b.Add(Format.Clock4(clk), _c.Unit);
            b.PadTo(Layout.F3);
            if (tempVis) Val(b, Format.Temp4(cpu.Temp!.Value), _c.Severity(cpu.Temp.Value, t.CpuTemp.Warn, t.CpuTemp.Crit));
            Graph(b, h.Values, 100, _c.Cpu);
            items["cpu.load"] = new() { b.Build() };
        }

        // Hot cores: load outliers, high load, or unusually hot. Every core keeps its graph history.
        bool anyCore = false;
        if (cpu.CoreLoads is { Length: > 0 } raw && (On("cpu.cores") || On("cpu.top")))
        {
            var smooth = _cores.Update(raw, _now);
            for (int i = 0; i < raw.Length; i++) Hist("core." + i).Add(Math.Clamp(raw[i], 0, 100));
            // Only outliers above the median: an idle core among busy ones is not "hot".
            var outliers = OutlierStats.Find(smooth, cpu.CoreGroups, t.CoreMinSigma, t.CoreSigmaFactor)
                .Where(o => o.Load > o.Median).Select(o => o.Index).ToHashSet();
            var temps = cpu.CoreTemps;
            double medianTemp = temps is { Length: > 0 } ? OutlierStats.Median(temps.Where(x => !double.IsNaN(x)).ToList()) : double.NaN;
            var hot = new List<HotCore>();
            for (int i = 0; i < smooth.Length; i++)
            {
                double temp = temps != null && i < temps.Length ? temps[i] : double.NaN;
                bool hotTemp = !double.IsNaN(temp) && (t.CpuTemp.Crossed(temp) || (!double.IsNaN(medianTemp) && temp - medianTemp >= t.HotCoreTempDelta));
                bool isHot = outliers.Contains(i) || smooth[i] >= t.HotCoreLoad || hotTemp;
                bool vis = _preview ? isHot || i == ArgMax(smooth) : _vis.Update("core." + i, isHot, _now);
                if (!vis) continue;
                double clock = cpu.CoreClocksMHz is { } cl && i < cl.Length ? cl[i] : 0;
                hot.Add(new HotCore(i, smooth[i], temp, clock, CoreGrid.Score(smooth[i], temp, medianTemp, t.CpuTemp.Warn, t.CpuTemp.Crit)));
            }
            anyCore = hot.Count > 0;
            if (On("cpu.cores")) items["cpu.cores"] = CoreGridLines(hot, f.CoreDetails);
        }
        else _cores.Reset();

        if (On("cpu.throttle") && cpu.Perf is { } perf)
        {
            bool cond = perf < t.ThrottlePerf && cpu.Load >= t.ThrottleMinLoad;
            if (Vis("cpu.throttle", cond))
            {
                var b = new LineBuilder();
                b.Add("  └ ", _c.Dim).Add("throttled", _c.Warn).Right(new Run("perf ", _c.Unit), new Run(Format.Pct4(perf), _c.Warn, ValueStyle));
                items["cpu.throttle"] = new() { b.Build() };
            }
        }

        if (On("cpu.top"))
        {
            bool cond = cpu.Load >= t.CpuTopProcessLoad || anyCore;
            bool vis = Vis("cpu.top", cond);
            Needs.TopCpu = cond || vis || _vis.IsPending("cpu.top") || _preview;
            var p = Sticky("cpu", vis ? s.TopCpu : null);
            if (vis && p != null) items["cpu.top"] = new() { ProcessLine(p) };
        }
    }

    private static int ArgMax(double[] v)
    {
        int best = 0;
        for (int i = 1; i < v.Length; i++) if (v[i] > v[best]) best = i;
        return best;
    }

    /// <summary>
    /// Hot cores in 1, 2 or 3 columns (at most 3 rows), btop style. More than 9 cores: the best 9 by
    /// score, and one line "n hot cores hidden, at y%" (one fully used core = 100 %).
    /// </summary>
    private List<Line> CoreGridLines(List<HotCore> hot, bool details)
    {
        var lines = new List<Line>();
        if (hot.Count == 0) return lines;
        var (shown, hidden) = CoreGrid.Select(hot);
        int cols = CoreGrid.Columns(shown.Count);
        int rows = (shown.Count + cols - 1) / cols;
        for (int r = 0; r < rows; r++)
        {
            if (cols == 1)
            {
                lines.Add(CoreLine(shown[r], details));
                continue;
            }
            var b = new LineBuilder();
            for (int c = 0; c < cols; c++)
            {
                int k = r + c * rows; // fill column by column, so numbers read top to bottom
                if (k >= shown.Count) break;
                if (c > 0) b.PadTo(c * (cols == 2 ? 16 : 11));
                CoreCell(b, shown[k], cols, details);
            }
            lines.Add(b.Build());
        }
        if (hidden.Count > 0)
        {
            var b = new LineBuilder();
            b.Add("  ", _c.Dim).Add($"{hidden.Count} hot cores hidden, at ", _c.Unit);
            Val(b, Format.Int(CoreGrid.CoreSum(hidden)) + "%");
            lines.Add(b.Build());
        }
        return lines;
    }

    private Rgb TempColor(double temp)
    {
        var t = _cfg.Thresholds;
        return double.IsNaN(temp) ? _c.Label : _c.Severity(temp, t.CpuTemp.Warn, t.CpuTemp.Crit, _c.Label);
    }

    /// <summary>One column: "c7   95% 4.8G  82° ⣀⣠⣴⣶⣿⣿…" — load, clock, temperature, braille graph.</summary>
    private Line CoreLine(HotCore core, bool details)
    {
        var t = _cfg.Thresholds;
        var b = Label("c" + core.Index, TempColor(core.Temp));
        Val(b, Format.Pct4(core.Load));
        if (details)
        {
            b.PadTo(Layout.F2);
            if (core.ClockMHz > 0) b.Add(Format.Clock4(core.ClockMHz), _c.Unit);
            b.PadTo(Layout.F3);
            if (!double.IsNaN(core.Temp)) Val(b, Format.Temp4(core.Temp), _c.Severity(core.Temp, t.CpuTemp.Warn, t.CpuTemp.Crit));
        }
        Graph(b, Hist("core." + core.Index).Values, 100, _c.Cpu);
        return b.Build();
    }

    /// <summary>
    /// Grid cell. 2 columns (16 wide): "c7  ⣴⣶⣿ 95% 82° "; 3 columns (10 wide + 1 gap): "c7  ⣶⣿ 95%".
    /// The label colour shows the temperature (normal → warning → critical).
    /// </summary>
    private void CoreCell(LineBuilder b, HotCore core, int cols, bool details)
    {
        var t = _cfg.Thresholds;
        string label = "c" + core.Index;
        b.Add(label.Length > 3 ? label[..3] : label.PadRight(3), TempColor(core.Temp)).Add(" ", _c.Dim);
        int graphChars = cols == 2 ? 3 : 2;
        var (text, max) = Glyphs.Graph(Hist("core." + core.Index).Values, graphChars, 100);
        for (int i = 0; i < text.Length; i++)
            b.Add(text[i].ToString(), double.IsNaN(max[i]) ? _c.Dim : _c.GraphCell(max[i] / 100, _c.Cpu));
        Val(b, Format.Pct4(core.Load));
        if (cols == 2 && details && !double.IsNaN(core.Temp))
            Val(b, Format.Temp4(core.Temp), _c.Severity(core.Temp, t.CpuTemp.Warn, t.CpuTemp.Crit));
    }

    // ───────────── Network ─────────────

    private void ComposeNet(Snapshot s, Dictionary<string, List<Line>> items)
    {
        if (s.Net is { } net)
        {
            bool bytes = _cfg.Fields.NetBytes;
            double down = net.Sum(a => a.DownBps), up = net.Sum(a => a.UpBps);
            var hd = Hist("net.down");
            hd.Add(down);
            var hu = Hist("net.up");
            hu.Add(up);
            if (On("net.down")) items["net.down"] = new() { NetLine("↓", down, hd, _c.NetDown, bytes) };
            if (On("net.up")) items["net.up"] = new() { NetLine("↑", up, hu, _c.NetUp, bytes) };

            // Adapter names: only when more than one physical adapter has traffic.
            var active = net.Where(a => a.DownBps + a.UpBps >= AdapterTrafficBps).ToList();
            if (On("net.adapters") && Vis("net.multi", active.Count > 1))
            {
                var lines = new List<Line>();
                foreach (var a in net.OrderByDescending(a => a.DownBps + a.UpBps))
                {
                    var b = new LineBuilder();
                    b.Add("  └ ", _c.Dim).Add(Format.Fit(a.Name, 10), _c.Label)
                     .Add(" ↓", _c.NetDown).Add(Format.Rate4(a.DownBps, bytes), _c.Text)
                     .Add(" ↑", _c.NetUp).Add(Format.Rate4(a.UpBps, bytes), _c.Text);
                    lines.Add(b.Build());
                }
                items["net.adapters"] = lines;
            }
        }

        if (On("net.quality") && s.Quality is { } q)
        {
            var t = _cfg.Thresholds;
            var lines = new List<Line>();
            bool gwLoss = q.HasGateway && t.PacketLoss.Crossed(q.GatewayLoss);
            bool netLoss = t.PacketLoss.Crossed(q.InternetLoss);
            if (Vis("net.loss", gwLoss || netLoss))
            {
                double loss = gwLoss || (_preview && !netLoss) ? q.GatewayLoss : q.InternetLoss;
                var b = Label("loss", _c.Label);
                Val(b, Format.Pct4(loss), _c.Severity(loss, t.PacketLoss.Warn, t.PacketLoss.Crit)).Add(gwLoss ? "  local network" : "  internet (isp)", _c.Unit);
                lines.Add(b.Build());
            }
            if (q.GatewayMs is { } gms && Vis("net.gwping", t.GatewayLatency.Crossed(gms))) lines.Add(PingLine("lan", gms, t.GatewayLatency));
            if (q.InternetMs is { } ims && Vis("net.netping", t.InternetLatency.Crossed(ims))) lines.Add(PingLine("isp", ims, t.InternetLatency));
            items["net.quality"] = lines;
        }
    }

    private Line PingLine(string where, double ms, Threshold th)
    {
        var b = Label("ping", _c.Label);
        Val(b, Format.Num4(ms), _c.Severity(ms, th.Warn, th.Crit)).Add(" ms  " + where, _c.Unit);
        return b.Build();
    }

    /// <summary>"↓     61M   pk  98M ⣀⣠⣤…" — value, peak of the graph window, graph scaled to the peak.</summary>
    private Line NetLine(string arrow, double bps, History h, Rgb accent, bool bytes)
    {
        double peak = Math.Max(h.Max, bps);
        var b = Label(arrow, accent);
        Val(b, Format.Rate4(bps, bytes));
        b.PadTo(Layout.F2).Add("  pk", _c.Unit);
        b.PadTo(Layout.F3).Add(Format.Rate4(peak, bytes), _c.Unit);
        Graph(b, h.Values, peak <= 0 ? 1 : peak, accent);
        return b.Build();
    }

    // ───────────── GPU ─────────────

    private void ComposeGpu(Snapshot s, Dictionary<string, List<Line>> items)
    {
        if (s.Gpus is not { Count: > 0 } gpus) return;
        var t = _cfg.Thresholds;
        var labels = GpuNames.Labels(gpus.Select(g => (g.Name, g.Integrated)).ToList());
        var loadLines = new List<Line>();
        var vramLines = new List<Line>();
        var topLines = new List<Line>();
        for (int i = 0; i < gpus.Count; i++)
        {
            var g = gpus[i];
            string key = "gpu." + g.Luid;
            if (g.Load is { } gl) Hist(key).Add(Math.Clamp(gl, 0, 100));

            bool loadVis = g.Load is { } l && Vis(key + ".load", l >= t.GpuLoad);
            bool tempVis = _cfg.Fields.GpuTemp && g.Temp is { } tc && Vis(key + ".temp", t.GpuTemp.Crossed(tc));
            if (On("gpu.load") && (loadVis || tempVis))
            {
                var b = Label(labels[i], _c.Gpu);
                if (loadVis) Val(b, Format.Pct4(g.Load!.Value));
                b.PadTo(Layout.F3);
                if (tempVis) Val(b, Format.Temp4(g.Temp!.Value), _c.Severity(g.Temp.Value, t.GpuTemp.Warn, t.GpuTemp.Crit));
                if (loadVis) Graph(b, Hist(key).Values, 100, _c.Gpu);
                loadLines.Add(b.Build());
            }

            double vramPct = g.VramUsedBytes is { } vu && g.VramTotalBytes is { } vt && vt > 0 ? 100 * vu / vt : double.NaN;
            if (On("gpu.vram") && !g.Integrated && !double.IsNaN(vramPct) && Vis(key + ".vram", t.Vram.Crossed(vramPct)))
            {
                var c = _c.Severity(vramPct, t.Vram.Warn, t.Vram.Crit);
                var b = Label("vram", _c.Label);
                Val(b, Format.Bytes4(g.VramUsedBytes!.Value), c).Add(" / " + Format.Bytes4(g.VramTotalBytes!.Value).Trim(), _c.Unit)
                    .Right(new Run(Format.Pct4(vramPct), c, ValueStyle));
                vramLines.Add(b.Build());
            }

            if (On("gpu.top") && g.Load is { } l2)
            {
                bool vis = Vis(key + ".top", l2 >= t.GpuTopProcessLoad);
                var p = Sticky(key, vis ? g.Tops : null);
                if (vis && p != null) topLines.Add(ProcessLine(p));
            }
        }
        items["gpu.load"] = loadLines;
        items["gpu.vram"] = vramLines;
        items["gpu.top"] = topLines;
    }

    // ───────────── Memory ─────────────

    private void ComposeMemory(Snapshot s, Dictionary<string, List<Line>> items)
    {
        Needs.TopMem = false;
        if (s.Mem is not { } m) return;
        var t = _cfg.Thresholds;
        bool any = false, pending = false;

        double availPct = m.TotalBytes > 0 ? 100.0 * m.AvailBytes / m.TotalBytes : double.NaN;
        if (On("mem.avail"))
        {
            bool cond = !double.IsNaN(availPct) && t.AvailableRam.Crossed(availPct);
            if (Vis("mem.avail", cond))
            {
                any = true;
                var c = _c.Severity(availPct, t.AvailableRam.Warn, t.AvailableRam.Crit);
                var b = Label("ram", _c.Memory);
                Val(b, Format.Bytes4(m.AvailBytes), c).Add(" available", _c.Unit).Right(new Run(Format.Pct4(availPct), c, ValueStyle));
                items["mem.avail"] = new() { b.Build() };
            }
            pending |= cond || _vis.IsPending("mem.avail");
        }

        double commitPct = m.CommitLimitBytes > 0 ? 100.0 * m.CommitBytes / m.CommitLimitBytes : double.NaN;
        if (On("mem.commit"))
        {
            bool cond = !double.IsNaN(commitPct) && t.Commit.Crossed(commitPct);
            if (Vis("mem.commit", cond))
            {
                any = true;
                var c = _c.Severity(commitPct, t.Commit.Warn, t.Commit.Crit);
                var b = Label("cmt", _c.Memory);
                Val(b, Format.Bytes4(m.CommitBytes), c).Add(" / " + Format.Bytes4(m.CommitLimitBytes).Trim() + " commit", _c.Unit)
                    .Right(new Run(Format.Pct4(commitPct), c, ValueStyle));
                items["mem.commit"] = new() { b.Build() };
            }
            pending |= cond || _vis.IsPending("mem.commit");
        }

        if (On("mem.pagein") && m.PagesInPerSec is { } pf)
        {
            bool cond = t.PageFaults.Crossed(pf);
            if (Vis("mem.pf", cond))
            {
                any = true;
                var b = Label("pgin", _c.Memory);
                Val(b, Format.Count4(pf), _c.Severity(pf, t.PageFaults.Warn, t.PageFaults.Crit)).Add(" /s hard faults", _c.Unit);
                items["mem.pagein"] = new() { b.Build() };
            }
            pending |= cond || _vis.IsPending("mem.pf");
        }

        if (On("mem.top"))
        {
            Needs.TopMem = any || pending || _preview;
            var p = Sticky("mem", any ? s.TopMem : null);
            if (any && p != null) items["mem.top"] = new() { ProcessLine(p) };
        }
    }

    // ───────────── Disk ─────────────

    private void ComposeDisk(Snapshot s, Dictionary<string, List<Line>> items)
    {
        Needs.TopDisk = false;
        var t = _cfg.Thresholds;
        bool anyBusy = false, pending = false;

        if (On("disk.busy") && s.Disks != null)
        {
            var lines = new List<Line>();
            foreach (var d in s.Disks)
            {
                string key = "disk." + d.Label;
                bool cond = t.DiskActive.Crossed(d.ActivePercent) || t.DiskQueue.Crossed(d.Queue);
                bool vis = Vis(key, cond);
                pending |= cond || _vis.IsPending(key);
                if (!vis) continue;
                anyBusy = true;
                var b = Label(d.Label, _c.Disk);
                Val(b, Format.Pct4(d.ActivePercent), _c.Severity(d.ActivePercent, t.DiskActive.Warn, t.DiskActive.Crit));
                b.Add(" q", _c.Unit);
                Val(b, Format.Num4(d.Queue), _c.Severity(d.Queue, t.DiskQueue.Warn, t.DiskQueue.Crit));
                b.Add("  " + d.Volumes, _c.Unit);
                lines.Add(b.Build());
            }
            items["disk.busy"] = lines;
        }

        if (On("disk.free") && s.Volumes != null)
        {
            var lines = new List<Line>();
            foreach (var v in s.Volumes)
            {
                if (v.TotalBytes <= 0) continue;
                double pct = 100 * v.FreeBytes / v.TotalBytes;
                double gb = v.FreeBytes / (1024.0 * 1024 * 1024);
                bool lowGb = gb < t.FreeSpaceGb;
                bool cond = t.FreeSpacePercent.Crossed(pct) || lowGb;
                if (!Vis("vol." + v.Root, cond)) continue;
                double sev = Math.Max(Gradient.Severity(pct, t.FreeSpacePercent.Warn, t.FreeSpacePercent.Crit), lowGb ? 1 : 0);
                var c = _c.SeverityOf(sev);
                var b = Label(v.Root.TrimEnd('\\'), _c.Disk);
                Val(b, Format.Bytes4(v.FreeBytes), c).Add(" free", _c.Unit).Right(new Run(Format.Pct4(pct), c, ValueStyle));
                lines.Add(b.Build());
            }
            items["disk.free"] = lines;
        }

        if (On("disk.top"))
        {
            Needs.TopDisk = pending || anyBusy || _preview;
            var p = Sticky("disk", anyBusy ? s.TopDisk : null);
            if (anyBusy && p != null) items["disk.top"] = new() { ProcessLine(p) };
        }
    }

    // ───────────── Claude ─────────────

    private void ComposeClaude(Snapshot s, Dictionary<string, List<Line>> items)
    {
        if (s.Claude is not { Count: > 0 } cl) return;
        var proj = _cfg.Thresholds.ClaudeProjection;
        bool projection = _cfg.Fields.ClaudeProjection;
        foreach (var c in cl)
        {
            string id = c.Label switch { "5h" => "claude.5h", "wk" => "claude.week", _ => "claude.models" };
            if (!On(id)) continue;
            if (!items.TryGetValue(id, out var list)) items[id] = list = new();
            list.AddRange(ClaudeLines(c, proj, projection));
        }
    }

    /// <summary>
    /// "5h   ::::::::::::::::::::::  42%"
    /// "  ↻ refresh ············ 3h10m"
    /// "  ◷ will last"  or  "  ⚠ empty 1d22h before reset"
    /// </summary>
    public IEnumerable<Line> ClaudeLines(ClaudeLine c, Threshold projection, bool showProjection = true)
    {
        const int barWidth = 22;
        var theme = _c.Theme;
        var b = Label(c.Label, _c.Claude);
        var (bar, filled) = Glyphs.StyledBar(c.Used / 100.0, barWidth, theme.BarStyle);
        for (int i = 0; i < bar.Length; i++)
        {
            bool on = i < filled;
            var color = !on ? _c.Dim : theme.BarGradient ? Rgb.Stops(_c.GraphStops, (i + 0.5) / barWidth) : _c.Claude;
            b.Add(bar[i].ToString(), color, on && theme.BoldValues ? RunStyle.Bold : RunStyle.None);
        }
        b.Add(" ", _c.Dim);
        Val(b, Format.Pct4(c.Used));
        yield return b.Build();

        if (c.ResetIn is { } reset)
        {
            var r = new LineBuilder();
            r.Add("  ", _c.Dim).Icon(Icons.Refresh, _c.Icon).Add("refresh ", _c.Unit);
            string d = Format.Duration(reset);
            while (r.Column < Layout.Width - d.Length - 1) r.Add("·", _c.Dim);
            r.Add(" ", _c.Dim);
            Val(r, d);
            yield return r.Build();
        }

        if (!showProjection) yield break;
        var p = c.Projection;
        var l = new LineBuilder();
        l.Add("  ", _c.Dim);
        switch (p.Kind)
        {
            case ProjectionKind.Lasts:
                l.Icon(Icons.Clock, _c.Icon).Add("will last", _c.Unit);
                break;
            case ProjectionKind.Limit:
            {
                var color = _c.Severity(p.Ratio, projection.Warn, projection.Crit);
                var empty = p.TimeToReset - p.TimeToLimit;
                l.Icon(Icons.Warning, color).Add("empty ", color);
                Val(l, Format.Duration(empty), color);
                l.Add(" before reset", color);
                break;
            }
            default:
                l.Icon(Icons.Clock, _c.Dim).Add("too early to project", _c.Dim);
                break;
        }
        yield return l.Build();
    }
}
