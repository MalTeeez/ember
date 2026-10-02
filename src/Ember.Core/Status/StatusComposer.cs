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
    private readonly DataGate _gate = new();
    // Peaks of the last hour, to colour the network numbers by how high they are.
    private readonly Dictionary<string, SlidingPeak> _hourPeak = new();
    private readonly Dictionary<string, MinuteSum> _pkMinute = new();

    private SlidingPeak HourPeak(string key, double value)
    {
        if (!_hourPeak.TryGetValue(key, out var p)) _hourPeak[key] = p = new SlidingPeak();
        p.Add(_now, value);
        return p;
    }
    private readonly HashSet<string> _gone = new();
    private int _usageLabelWidth = 5;
    private ThemeColors _c = ThemeColors.From(new Theme());
    private Theme? _themeRef;
    private EmberSettings _cfg = new();
    private bool _preview;
    private DateTime _now;
    private TimeSpan _stick;
    public Needs Needs { get; } = new();

    private Action<string>? _trace;
    /// <summary>Decision log (extended debug): show/hide, kept space, top process changes, widget height.</summary>
    public Action<string>? Trace
    {
        get => _trace;
        set
        {
            _trace = value;
            _vis.Trace = value;
            _keep.Trace = value;
        }
    }
    private int _lastCount = -1, _lastPadding = -1;
    private readonly Dictionary<string, int> _stickyPid = new();

    // Enough samples for the longest graph (from the label column to the right edge).
    private const int HistorySamples = (Layout.Width - Layout.LabelWidth) * 2;
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
        _gate.Clear();
        _hourPeak.Clear();
        _pkMinute.Clear();
        _usageLabelWidth = 5;
        foreach (var s in _sticky.Values) s.Reset();
    }

    private bool On(string id) => _cfg.IsOn(id);

    /// <summary>
    /// Threshold with a band: a visible item stays until the value falls <paramref name="margin"/> below
    /// the threshold, so values near the threshold do not make it show and hide again and again.
    /// </summary>
    private bool Above(string key, double value, double threshold, double margin)
        => value >= (_vis.IsVisible(key) ? threshold - margin : threshold);

    /// <summary>Show/hide state; in preview mode every condition is true at once.</summary>
    private bool Vis(string key, bool cond) => _preview || _vis.Update(key, cond, _now);

    private ProcInfo? Sticky(string key, IReadOnlyList<ProcInfo>? candidates)
    {
        if (!_sticky.TryGetValue(key, out var st)) _sticky[key] = st = new StickyTop();
        var p = st.Update(candidates, _now, _stick);
        if (_trace != null)
        {
            int pid = p?.Pid ?? 0, old = _stickyPid.GetValueOrDefault(key);
            if (pid != old)
            {
                _trace($"top process {key}: {(old == 0 ? "none" : old.ToString(System.Globalization.CultureInfo.InvariantCulture))} → {(p == null ? "none" : p.Name + " (" + pid + ")")}" +
                    (candidates is { Count: > 0 } && p != null && candidates[0].Pid != pid ? " (kept: the new top did not stay on top long enough)" : ""));
                _stickyPid[key] = pid;
            }
        }
        return p;
    }

    private EmberSettings? _cfgRef;

    public List<Line> Compose(Snapshot s, EmberSettings cfg, ComposeContext? ctx = null)
    {
        // New settings (the app gives a new object for each change): no kept space from before, so a
        // switched-off line or option goes away at once instead of after the hold time.
        if (_cfgRef != null && !ReferenceEquals(cfg, _cfgRef))
        {
            _keep.Clear();
            _trace?.Invoke("settings changed: kept space released");
        }
        _cfgRef = cfg;
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
        _gone.Clear();

        var items = new Dictionary<string, List<Line>>();
        if (On("toolbar")) items["toolbar"] = new() { Toolbar(ctx ?? new ComposeContext()) };
        ComposeCpu(s, items);
        ComposeNet(s, items);
        ComposeGpu(s, items);
        ComposeMemory(s, items);
        ComposeDisk(s, items);
        var claude = Gate("claude", s.Claude, "claude.5h", "claude.week", "claude.models");
        var codex = Gate("codex", s.Codex, "codex.5h", "codex.week");
        var copilot = Gate("copilot", s.Copilot, "copilot.premium", "copilot.other");
        // One label width for all usage bars, so all bars have the same length (it only grows).
        foreach (var l in new[] { claude, codex, copilot }.Where(u => u != null).SelectMany(u => u!))
            _usageLabelWidth = Math.Clamp(Math.Max(_usageLabelWidth, l.Label.Length + 1), 5, 9);
        ComposeUsage(claude, items, "claude.5h", "claude.week", "claude.models", _c.Claude);
        ComposeUsage(codex, items, "codex.5h", "codex.week", "codex.week", _c.Codex);
        ComposeUsage(copilot, items, "copilot.premium", "copilot.premium", "copilot.other", _c.Copilot);
        ComposeBatteries(s, items);

        _vis.EndTick();
        foreach (var k in _hist.Keys.Where(k => !_usedHist.Contains(k)).ToList()) _hist.Remove(k);

        return Arrange(items);
    }

    // ───────────── Layout ─────────────

    /// <summary>
    /// Places item lines in layout order, keeps heights, and shows headers/spacers of non-empty groups.
    /// Kept (empty) space goes to the bottom of the widget by default, so the lines that remain move up
    /// and stay together; option "inline" keeps the empty lines where the item was.
    /// </summary>
    private List<Line> Arrange(Dictionary<string, List<Line>> items)
    {
        var entries = _cfg.Entries;
        var hold = _preview ? TimeSpan.Zero : TimeSpan.FromSeconds(_cfg.Layout.HoldSeconds);
        string mode = _cfg.Layout.KeptSpace;
        bool bottom = mode == "bottom", section = mode == "section";
        var height = new int[entries.Count];
        var real = new int[entries.Count];
        var output = new List<Line>?[entries.Count];
        var pads = new int[entries.Count];
        int pad = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Kind != "item" || !e.Enabled) continue;
            if (_gone.Contains(e.Id))
            {
                // No data from its source: no kept space either, so the whole section goes.
                _keep.Forget(e.Id);
                output[i] = new List<Line>();
                continue;
            }
            var lines = items.GetValueOrDefault(e.Id) ?? new List<Line>();
            int h = _keep.Reserve(e.Id, lines.Count, _now, hold);
            var list = new List<Line>(lines);
            if (bottom) pad += h - lines.Count;
            else if (section) pads[i] = h - lines.Count;
            else while (list.Count < h) list.Add(Layout.Blank(padding: true));
            height[i] = h;
            real[i] = lines.Count;
            output[i] = list;
        }

        var result = new List<Line>();
        var decos = new HashSet<Line>(ReferenceEqualityComparer.Instance);
        // "section": the kept space of a section's items goes to the end of the section.
        int sectionPad = 0;
        void FlushSection()
        {
            for (int k = 0; k < sectionPad; k++) result.Add(Layout.Blank(padding: true));
            sectionPad = 0;
        }
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (!e.Enabled) continue;
            if (e.Kind == "item")
            {
                result.AddRange(output[i]!);
                sectionPad += pads[i];
                continue;
            }
            FlushSection();
            if (e.Collapse && GroupHeight(entries, height, i) == 0) continue;
            // A section that only keeps space: its header/spacer line is kept space too (at the bottom).
            if (bottom && e.Collapse && GroupHeight(entries, real, i) == 0) { pad++; continue; }
            var deco = e.Kind == "header" ? Layout.Header(e.Text ?? "", _c) : Layout.Spacer(e.Preset, e.Text, _c);
            if (e.Kind == "header" && _c.Theme.HeaderBand) deco.Back = _c.Band(_c.Header, 30);
            if (e.Kind == "header" && !_c.Theme.HeaderTextShadow) deco.NoShadow = true;
            result.Add(deco);
            decos.Add(deco);
        }
        FlushSection();
        TreeBranches(result);
        // Optional band behind the toolbar (first line), inset from the edges.
        if (_c.Theme.ToolbarBand && result.Count > 0 && result[0].Hits.Any(h => h.Action == HitAction.NewNote))
            result[0].Back = _c.Band(_c.Text, 26);
        if (_c.Theme.RowStripes) Stripe(result, decos);
        // No empty lines at the top or bottom of the widget.
        while (result.Count > 0 && IsEmpty(result[0]) && !result[0].IsPadding) result.RemoveAt(0);
        while (result.Count > 0 && IsEmpty(result[^1]) && !result[^1].IsPadding) result.RemoveAt(result.Count - 1);
        for (int i = 0; i < pad; i++) result.Add(Layout.Blank(padding: true));
        // The top row moves the widget (drag); its buttons still work (smaller hits win).
        if (result.Count > 0 && !result[0].Hits.Any(h => h.Width >= Layout.Width))
            result[0].Hits.Insert(0, new Hit(0, Layout.Width, HitAction.Drag, "", ""));

        if (_trace != null)
        {
            int padding = result.Count(l => l.IsPadding);
            if (result.Count != _lastCount || padding != _lastPadding)
            {
                _trace($"widget height {(_lastCount < 0 ? "-" : _lastCount.ToString(System.Globalization.CultureInfo.InvariantCulture))} → {result.Count} lines ({padding} kept empty{(bottom ? " at the bottom" : section ? " at section ends" : "")})");
                _lastCount = result.Count;
                _lastPadding = padding;
            }
        }
        return result;
    }

    private static bool IsEmpty(Line l) => l.Text.Trim().Length == 0 && l.Hits.Count == 0;

    /// <summary>
    /// Sub-lines as a tree: in a run of lines that start with "└" in the same column (only spaces before
    /// it), all but the last get "├", so the branch continues down to the last sub-line.
    /// </summary>
    public static void TreeBranches(List<Line> lines)
    {
        static int BranchCol(Line l)
        {
            if (l.IsPadding) return -1;
            string t = l.Text;
            int i = 0;
            while (i < t.Length && t[i] == ' ') i++;
            return i < t.Length && t[i] == '└' ? i : -1;
        }
        for (int i = 0; i + 1 < lines.Count; i++)
        {
            int col = BranchCol(lines[i]);
            if (col >= 0 && BranchCol(lines[i + 1]) == col) ReplaceChar(lines[i], col, '├');
        }
    }

    private static void ReplaceChar(Line l, int col, char ch)
    {
        int pos = 0;
        for (int k = 0; k < l.Runs.Count; k++)
        {
            var r = l.Runs[k];
            if (col < pos + r.Text.Length)
            {
                var chars = r.Text.ToCharArray();
                chars[col - pos] = ch;
                l.Runs[k] = r with { Text = new string(chars) };
                return;
            }
            pos += r.Text.Length;
        }
    }

    /// <summary>Every second content line of a section gets a faint band (counting restarts after a header or blank line).</summary>
    private void Stripe(List<Line> lines, HashSet<Line> decos)
    {
        int n = 0;
        foreach (var l in lines)
        {
            bool content = !l.IsPadding && !IsEmpty(l) && !l.Hits.Any(h => h.Action == HitAction.NewNote);
            if (!content || decos.Contains(l) || l.Back.Visible) { n = 0; continue; }
            if (n++ % 2 == 1) l.Back = _c.Band(_c.Text, 16);
        }
    }

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

    /// <summary>A value. In a warning/critical colour (not the text colour) it can get a chip behind it.</summary>
    private LineBuilder Val(LineBuilder b, string text, Rgb? color = null)
    {
        var c = color ?? _c.Text;
        bool chip = _c.Theme.ValueChips && c != _c.Text && text.Trim().Length > 0;
        if (!chip) return b.Add(text, c, ValueStyle);
        // The chip covers the text only, not the padding of the 4-character field.
        string core = text.Trim();
        int lead = text.Length - text.TrimStart().Length;
        if (lead > 0) b.Add(text[..lead], _c.Dim);
        b.Add(core, c, ValueStyle, _c.Band(c, 60));
        int trail = text.Length - lead - core.Length;
        if (trail > 0) b.Add(new string(' ', trail), _c.Dim);
        return b;
    }

    private Shade GraphBack => _c.Theme.GraphTrack ? _c.Band(_c.Dim, 50) : default;
    private Shade BarBack => _c.Theme.BarTrack ? _c.Band(_c.Dim, 50) : default;

    /// <summary>
    /// Braille graph from <paramref name="col"/> for <paramref name="chars"/> characters (default: to the right edge);
    /// each cell coloured by its value (intensity), the accent, or mono.
    /// </summary>
    private void Graph(LineBuilder b, IReadOnlyList<double> values, double scale, Rgb accent, int col = Layout.GraphCol, int chars = -1)
    {
        b.PadTo(col);
        if (chars < 0) chars = Layout.Width - b.Column;
        if (chars <= 0) return;
        var (text, max) = Glyphs.Graph(values, chars, scale);
        for (int i = 0; i < text.Length; i++)
            b.Add(text[i].ToString(), double.IsNaN(max[i]) ? _c.Dim : _c.GraphCell(scale > 0 ? max[i] / scale : 0, accent), RunStyle.None, GraphBack);
    }

    /// <summary>"  └ name.exe        58% ⓘ ⚙ ✕" — click = details; icons = details, file properties, end process.</summary>
    private Line ProcessLine(ProcInfo p)
    {
        string pid = p.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var b = new LineBuilder();
        b.LineHit(HitAction.ProcessDetails, pid, $"{p.Name} (PID {pid}) — details");
        string value = p.ValueText.Length > 6 ? p.ValueText[..6] : p.ValueText.PadLeft(4);
        int nameWidth = Layout.Width - 4 - value.Length - 1 - 7;
        b.Add("  └ ", _c.Dim).Add(Format.FitDots(Util.ProcessNames.StripExe(p.Name), nameWidth), _c.Process).Add(" ", _c.Dim);
        Val(b, value);
        b.Add(" ", _c.Dim);
        b.Button(Icons.Info, _c.Icon, HitAction.ProcessDetails, pid, "Details");
        b.Button(Icons.OpenFile, _c.Icon, HitAction.ProcessProperties, pid, "File properties");
        b.Button(Icons.Cancel, _c.Crit, HitAction.ProcessKill, pid, "End process");
        var line = b.Build();
        if (_c.Theme.ProcessBand) line.Back = _c.Band(_c.Process, 22);
        return line;
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
        bool tempVis = f.CpuTemp && cpu.Temp is { } temp0 && (Vis("cpu.temp", Above("cpu.temp", temp0, t.CpuTemp.Warn, 3)) | f.CpuTempAlways);

        if (On("cpu.load"))
        {
            // "cpu   83° ⣀⣠⣴⣶⣿…  45% 4.8GHz" — temperature, graph, load, clock. Without a shown
            // temperature (unknown, or only shown when hot) the graph takes its place.
            var b = Label("cpu", _c.Cpu);
            if (tempVis)
            {
                Val(b, Format.Temp4(cpu.Temp!.Value), _c.Severity(cpu.Temp.Value, t.CpuTemp.Warn, t.CpuTemp.Crit));
                b.PadTo(Layout.F2);
            }
            int right = Layout.FieldWidth + 1 + (f.CpuClock ? 7 : 0);
            Graph(b, h.Values, 100, _c.Cpu, b.Column, Layout.Width - right - b.Column);
            b.Add(" ", _c.Dim);
            Val(b, Format.Pct4(load), _c.Text);
            if (f.CpuClock) b.Add(" ", _c.Dim).Add(cpu.ClockMHz is { } clk ? Format.Hz6(clk) : "      ", _c.Unit);
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
                string ck = "core." + i;
                bool hotTemp = !double.IsNaN(temp) && (Above(ck, temp, t.CpuTemp.Warn, 3) || (!double.IsNaN(medianTemp) && Above(ck, temp - medianTemp, t.HotCoreTempDelta, 3)));
                bool isHot = outliers.Contains(i) || Above(ck, smooth[i], t.HotCoreLoad, 15) || hotTemp;
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
    /// Hot cores in 1, 2 or 3 columns (at most 3 rows), btop style, aligned right with an indent (they
    /// belong to the cpu line). Each core: label (colour = temperature), graph, load, and temperature
    /// (1 and 2 columns) and clock (1 column) when known. More than 9 cores: the best 9 by score, and
    /// one line "n hot cores hidden, at y%" (one fully used core = 100 %).
    /// </summary>
    private List<Line> CoreGridLines(List<HotCore> hot, bool details)
    {
        var lines = new List<Line>();
        if (hot.Count == 0) return lines;
        var (shown, hidden) = CoreGrid.Select(hot);
        int cols = CoreGrid.Columns(shown.Count);
        int rows = (shown.Count + cols - 1) / cols;
        bool temps = details && shown.Any(h => !double.IsNaN(h.Temp));
        bool clocks = details && shown.Any(h => h.ClockMHz > 0);
        // Cell width and gap: 1 column 30 (indent 2), 2 columns 15 + 1 + 15 (indent 1), 3 columns 9 + 1 + 9 + 1 + 9 (indent 2).
        var (cell, gap) = cols switch { 1 => (30, 0), 2 => (15, 1), _ => (9, 1) };
        int indent = Layout.Width - (cols * cell + (cols - 1) * gap);
        for (int r = 0; r < rows; r++)
        {
            var b = new LineBuilder();
            for (int c = 0; c < cols; c++)
            {
                int k = r + c * rows; // fill column by column, so numbers read top to bottom
                if (k >= shown.Count) break;
                b.PadTo(indent + c * (cell + gap));
                CoreCell(b, shown[k], cell, cols, temps, clocks);
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

    /// <summary>
    /// One core in a cell of <paramref name="width"/> characters:
    /// 1 column: "c7   83° ⣀⣠⣴⣶⣿…  95% 5.1GHz" (same right edge as the cpu line);
    /// 2 columns: "c18 ⣴⣶⣿  95% 83°" (graph longer without temperatures); 3 columns: "c7 ⣶⣿ 95%".
    /// </summary>
    private void CoreCell(LineBuilder b, HotCore core, int width, int cols, bool temps, bool clocks)
    {
        var t = _cfg.Thresholds;
        int start = b.Column;
        string label = "c" + core.Index;
        int labelW = cols == 3 ? 3 : 4;
        b.Add(label.Length > labelW ? label[..labelW] : label, TempColor(core.Temp)).PadTo(start + labelW);
        var tempRun = double.IsNaN(core.Temp) ? "    " : Format.Temp4(core.Temp);
        var tempColor = double.IsNaN(core.Temp) ? _c.Dim : _c.Severity(core.Temp, t.CpuTemp.Warn, t.CpuTemp.Crit);
        if (cols == 1 && temps)
        {
            Val(b, tempRun, tempColor);
            b.Add(" ", _c.Dim);
        }
        int right = Layout.FieldWidth + (cols == 1 ? 1 + (clocks ? 7 : 0) : 0) + (cols == 2 && temps ? Layout.FieldWidth : 0);
        int graph = start + width - right - b.Column;
        Graph(b, Hist("core." + core.Index).Values, 100, _c.Cpu, b.Column, graph);
        if (cols == 1) b.Add(" ", _c.Dim);
        Val(b, Format.Pct4(core.Load));
        if (cols == 1 && clocks) b.Add(" ", _c.Dim).Add(core.ClockMHz > 0 ? Format.Hz6(core.ClockMHz) : "      ", _c.Unit);
        if (cols == 2 && temps) Val(b, tempRun, tempColor);
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
            double downPk = net.Sum(a => a.DownPps), upPk = net.Sum(a => a.UpPps);
            // Packets: count of the last minute (pkt/m).
            if (!_pkMinute.TryGetValue("down", out var dm)) _pkMinute["down"] = dm = new MinuteSum();
            if (!_pkMinute.TryGetValue("up", out var um)) _pkMinute["up"] = um = new MinuteSum();
            double downMin = dm.Add(_now, downPk), upMin = um.Add(_now, upPk);
            if (On("net.down")) items["net.down"] = new() { NetLine("↓", down, downMin, hd, _c.NetDown, bytes, HourPeak("down", down), HourPeak("down.pk", downMin)) };
            if (On("net.up")) items["net.up"] = new() { NetLine("↑", up, upMin, hu, _c.NetUp, bytes, HourPeak("up", up), HourPeak("up.pk", upMin)) };

            // Adapter names: only when more than one physical adapter has traffic.
            var active = net.Where(a => a.DownBps + a.UpBps >= AdapterTrafficBps).ToList();
            if (On("net.adapters") && Vis("net.multi", active.Count > 1))
            {
                var lines = new List<Line>();
                foreach (var a in net.OrderByDescending(a => a.DownBps + a.UpBps))
                {
                    var b = new LineBuilder();
                    b.Add("  └ ", _c.Dim).Add(Format.Fit(a.Name, 10), _c.Label)
                     .Add(" ↓ ", _c.NetDown).Add(Format.Rate4(a.DownBps, bytes), _c.Text)
                     .Add(" ↑ ", _c.NetUp).Add(Format.Rate4(a.UpBps, bytes), _c.Text);
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
                Val(b, Format.Pct4(loss), _c.Severity(loss, t.PacketLoss.Warn, t.PacketLoss.Crit)).Add(gwLoss ? "  local network" : "  " + (_cfg.Network.ShowPingHost && q.InternetHost.Length > 0 ? q.InternetHost : "internet"), _c.Unit);
                lines.Add(b.Build());
            }
            bool always = _cfg.Network.PingAlways;
            if (_cfg.Network.ShowGatewayPing && q.GatewayMs is { } gms && (Vis("net.gwping", t.GatewayLatency.Crossed(gms)) | always))
                lines.Add(PingLine("lan", q.GatewayHost, gms, t.GatewayLatency));
            if (q.InternetMs is { } ims && (Vis("net.netping", t.InternetLatency.Crossed(ims)) | always))
            {
                lines.Add(PingLine("net", q.InternetHost, ims, t.InternetLatency));
                if (_cfg.Network.ShowTraceroute && q.Hops is { Count: > 0 } hops) lines.AddRange(HopLines(hops, q.InternetHost, t.InternetLatency));
            }
            items["net.quality"] = lines;
        }
    }

    /// <summary>
    /// Traceroute hops: "  └  1  192.168.1.1     2 ms"; hops without an answer are left out and the next
    /// answering hop gets a "*" ("  └ *4  10.12.0.1    11 ms").
    /// </summary>
    private IEnumerable<Line> HopLines(IReadOnlyList<TraceHop> hops, string target, Threshold th)
    {
        foreach (var (hop, skipped) in Traceroute.Collapse(hops, target))
        {
            var b = new LineBuilder();
            string ttl = hop.Ttl.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string tip = $"Hop {ttl}: {hop.Address}" + (hop.HostName is { Length: > 0 } hn ? $" ({hn})" : "") +
                         (hop.Ms is { } rt ? $", {rt:0.#} ms" : "") +
                         (skipped > 0 ? $"\n{skipped} hop{(skipped == 1 ? "" : "s")} before it did not answer" : "") +
                         (hop.Address == target ? "\nTarget" : "");
            b.LineHit(HitAction.None, hop.Address ?? "", tip);
            b.Add("  └ ", _c.Dim).Add(skipped > 0 ? "*" : " ", _c.Warn).Add(ttl.PadLeft(2), _c.Unit).Add(" ", _c.Dim);
            // The address gets all space up to the value (long IPv6 addresses); one space before the value.
            string value = hop.Ms is { } v ? Format.Num4(v).Trim() : "";
            int valueWidth = value.Length > 0 ? 1 + value.Length + 3 : 0;
            b.Add(Format.FitDots(hop.Address ?? "?", Layout.Width - b.Column - valueWidth).TrimEnd(), _c.Label);
            if (hop.Ms is { } m)
                b.Right(new Run(" ", _c.Dim), new Run(value, _c.Severity(m, th.Warn, th.Crit), ValueStyle), new Run(" ms", _c.Unit));
            yield return b.Build();
        }
    }

    /// <summary>"ping  12 ms  192.168.1.1" (local gateway) or "ping  31 ms  1.1.1.1" (internet host); without the host: "lan"/"net".</summary>
    private Line PingLine(string where, string host, double ms, Threshold th)
    {
        var b = Label("ping", _c.Label);
        Val(b, Format.Num4(ms), _c.Severity(ms, th.Warn, th.Crit)).Add(" ms  ", _c.Unit);
        b.Add(_cfg.Network.ShowPingHost && host.Length > 0 ? Format.FitDots(host, Layout.Width - b.Column).TrimEnd() : where, _c.Unit);
        return b.Build();
    }

    /// <summary>
    /// "↓  61M ⣀⣠⣤⣴⣶⣿… 457k pkt/m" — rate, graph (scaled to its peak), packets of the last minute. The numbers are
    /// coloured by how high they are compared to the highest value of the last hour.
    /// </summary>
    private Line NetLine(string arrow, double bps, double pps, History h, Rgb accent, bool bytes, SlidingPeak hour, SlidingPeak hourPk)
    {
        double peak = Math.Max(h.Max, bps);
        var b = new LineBuilder();
        b.Add(arrow + " ", accent);
        Val(b, Format.Rate4(bps, bytes), _c.GraphCell(hour.Fraction(bps), accent));
        b.Add(" ", _c.Dim);
        string pkt = Format.Pkt(pps);
        Graph(b, h.Values, peak <= 0 ? 1 : peak, accent, b.Column, Layout.Width - b.Column - pkt.Length - 1);
        b.Add(" ", _c.Dim).Add(Format.Count4(pps), _c.GraphCell(hourPk.Fraction(pps), accent)).Add(" pkt/m", _c.Dim);
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

            bool loadVis = g.Load is { } l && Vis(key + ".load", Above(key + ".load", l, t.GpuLoad, Math.Min(5, t.GpuLoad / 2)));
            bool tempVis = _cfg.Fields.GpuTemp && g.Temp is { } tc && Vis(key + ".temp", Above(key + ".temp", tc, t.GpuTemp.Warn, 3));
            if (On("gpu.load") && (loadVis || tempVis))
            {
                // "4090  72° ⣀⣠⣴⣶⣿…  95%" — temperature (when shown), graph, load (like the cpu line).
                var b = Label(labels[i], _c.Gpu);
                if (tempVis)
                {
                    Val(b, Format.Temp4(g.Temp!.Value), _c.Severity(g.Temp.Value, t.GpuTemp.Warn, t.GpuTemp.Crit));
                    b.PadTo(Layout.F2);
                }
                if (loadVis)
                {
                    Graph(b, Hist(key).Values, 100, _c.Gpu, b.Column, Layout.Width - Layout.FieldWidth - 1 - b.Column);
                    b.Add(" ", _c.Dim);
                    Val(b, Format.Pct4(g.Load!.Value));
                }
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
                bool vis = Vis(key + ".top", Above(key + ".top", l2, t.GpuTopProcessLoad, 15));
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
        Needs.Faults = false;
        if (s.Mem is not { } m) return;
        var t = _cfg.Thresholds;
        bool any = false, pending = false;

        // RAM: shown when little is available; the line shows the used part (easier to read with a bar).
        double availPct = m.TotalBytes > 0 ? 100.0 * m.AvailBytes / m.TotalBytes : double.NaN;
        if (On("mem.avail"))
        {
            bool cond = !double.IsNaN(availPct) && t.AvailableRam.Crossed(availPct);
            if (Vis("mem.avail", cond))
            {
                any = true;
                var c = _c.Severity(availPct, t.AvailableRam.Warn, t.AvailableRam.Crit);
                items["mem.avail"] = new() { AmountLine("used", m.TotalBytes - m.AvailBytes, m.TotalBytes, c) };
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
                // Commit = RAM + page file in use; the page file has its own line under it.
                var lines = new List<Line> { AmountLine("committed", m.CommitBytes, m.CommitLimitBytes, c) };
                if (m.PageFileTotalBytes is { } pft && pft > 0 && m.PageFileUsedBytes is { } pfu)
                {
                    double pfPct = 100.0 * pfu / pft;
                    lines.Add(AmountLine("pagefile", pfu, pft, _c.Severity(pfPct, t.Commit.Warn, t.Commit.Crit)));
                }
                items["mem.commit"] = lines;
            }
            pending |= cond || _vis.IsPending("mem.commit");
        }

        if (On("mem.pagein") && m.PagesInPerSec is { } pf)
        {
            // "hard disk paging ⣀⣠⣴⣶⣿  983/s" — pages read from disk (hard page faults) per second,
            // then the processes that cause them. Without hard paging: "soft disk paging" (soft faults).
            var h = Hist("mem.pf");
            h.Add(Math.Max(0, pf));
            double soft = m.SoftFaultsPerSec ?? 0;
            var hs = Hist("mem.soft");
            hs.Add(soft);
            bool cond = t.PageFaults.Crossed(pf);
            bool vis = Vis("mem.pf", cond);
            bool hardActive = cond || vis || _vis.IsPending("mem.pf");
            bool softCond = !hardActive && m.SoftFaultsPerSec != null && Above("mem.soft", soft, t.SoftPageFaults.Warn, t.SoftPageFaults.Warn * 0.3);
            bool softVis = !hardActive && Vis("mem.soft", softCond);
            Needs.Faults = hardActive || softCond || softVis || _vis.IsPending("mem.soft") || _preview;
            if (vis || softVis)
            {
                any = true;
                bool hard = vis;
                var th = hard ? t.PageFaults : t.SoftPageFaults;
                double value = hard ? pf : soft;
                var hist = hard ? h : hs;
                var b = new LineBuilder();
                b.Add(hard ? "hard disk paging" : "soft disk paging", _c.Memory);
                string text = Format.Count4(value) + "/s";
                Graph(b, hist.Values, Math.Max(hist.Max, th.Crit), _c.Memory, b.Column + 1, Layout.Width - text.Length - 1 - b.Column - 1);
                b.Add(" ", _c.Dim);
                Val(b, text, _c.Severity(value, th.Warn, th.Crit));
                var lines = new List<Line> { b.Build() };
                lines.AddRange(FaultLines(s.FaultProcs, hard ? s.FaultsTotal : s.SoftFaultsTotal, m.TotalBytes, hard));
                items["mem.pagein"] = lines;
            }
            pending |= cond || _vis.IsPending("mem.pf") || softCond || _vis.IsPending("mem.soft");
        }

        if (On("mem.top"))
        {
            // The top memory process belongs to the RAM/commit lines, not to paging (paging lists its own processes).
            bool usage = items.ContainsKey("mem.avail") || items.ContainsKey("mem.commit");
            bool usagePending = _vis.IsPending("mem.avail") || _vis.IsPending("mem.commit");
            Needs.TopMem = usage || usagePending || _preview;
            var p = Sticky("mem", usage ? s.TopMem : null);
            if (usage && p != null) items["mem.top"] = new() { ProcessLine(p) };
        }
    }

    /// <summary>"commit  35GB / 40GB ▮▮▮▮▮▯  87%" — name, amount of total, bar, percent.</summary>
    private Line AmountLine(string name, double amount, double total, Rgb color)
    {
        var b = new LineBuilder();
        b.Add(name, _c.Memory).PadTo(10); // "used", "committed", "pagefile": same columns
        return AmountRest(b, amount, total, color);
    }

    /// <summary>"35GB / 40GB ▮▮▮▮▮▯  87%" after a name: amount, total, bar to the right edge, percent.</summary>
    private Line AmountRest(LineBuilder b, double amount, double total, Rgb color, bool highIsGood = false)
    {
        double pct = total > 0 ? 100 * amount / total : 0;
        Val(b, Format.BytesB(amount), color);
        b.Add(" / " + Format.BytesB(total).Trim(), _c.Unit).Add(" ", _c.Dim);
        int barWidth = Layout.Width - b.Column - 1 - Layout.FieldWidth;
        LevelBar(b, pct / 100, Math.Max(1, barWidth), highIsGood, color);
        b.Add(" ", _c.Dim);
        Val(b, Format.Pct4(pct), color);
        return b.Build();
    }

    /// <summary>
    /// A level bar (memory, disk space, batteries). Filled cells: the warning/critical colour when the
    /// value crossed a threshold; else the graph colours by the level (how bad it is), or along the
    /// bar (by position) with the theme's "bar gradient". Empty cells dim.
    /// </summary>
    private void LevelBar(LineBuilder b, double fraction, int width, bool highIsGood, Rgb severityColor)
    {
        var theme = _c.Theme;
        var (bar, filled) = Glyphs.StyledBar(Math.Clamp(fraction, 0, 1), width, theme.BarStyle);
        double badness = highIsGood ? 1 - fraction : fraction;
        for (int i = 0; i < bar.Length; i++)
        {
            Rgb cell;
            if (i >= filled) cell = _c.Dim;
            else if (severityColor != _c.Text) cell = severityColor;
            else if (theme.BarGradient)
            {
                double pos = (i + 0.5) / width;
                cell = Rgb.Stops(_c.GraphStops, highIsGood ? 1 - pos : pos);
            }
            else cell = Rgb.Stops(_c.GraphStops, Math.Clamp(badness, 0, 1));
            b.Add(bar[i].ToString(), cell, RunStyle.None, BarBack);
        }
    }

    private const int MaxFaultLines = 5;

    /// <summary>
    /// " └ chrome.exe      1.4GB  320/s" — each process with hard page faults: private memory (colour =
    /// its share of the RAM) and hard faults per second (colour = its share of all hard faults).
    /// Click = details, right-click = process menu.
    /// </summary>
    private IEnumerable<Line> FaultLines(IReadOnlyList<FaultProc>? all, double total, double ram, bool hard)
    {
        double Rate(FaultProc p) => hard ? p.HardFaultsPerSec : p.SoftFaultsPerSec;
        var procs = all?.Where(p => Rate(p) >= (hard ? 1 : 100)).OrderByDescending(Rate).ToList();
        if (procs is not { Count: > 0 }) yield break;
        double sum = Math.Max(total, procs.Sum(Rate));
        foreach (var p in procs.Take(MaxFaultLines))
        {
            string pid = p.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var b = new LineBuilder();
            b.LineHit(HitAction.ProcessDetails, pid, $"{p.Name} (PID {pid}): {Format.Count4(Rate(p)).Trim()} {(hard ? "hard" : "soft")} page faults/s");
            string faults = Format.Count4(Rate(p)) + "/s";
            int nameWidth = Layout.Width - 3 - 1 - 5 - 1 - faults.Length;
            b.Add(" └ ", _c.Dim).Add(Format.FitDots(Util.ProcessNames.StripExe(p.Name), nameWidth), _c.Process).PadTo(3 + nameWidth + 1);
            // Memory: full colour at a quarter of the RAM.
            double memShare = ram > 0 ? Math.Min(1, 4 * p.PrivateBytes / ram) : 0;
            Val(b, Format.BytesB(p.PrivateBytes), Rgb.Stops(_c.GraphStops, memShare));
            b.Add(" ", _c.Dim);
            Val(b, faults, Rgb.Stops(_c.GraphStops, sum > 0 ? Rate(p) / sum : 0));
            var line = b.Build();
            if (_c.Theme.ProcessBand) line.Back = _c.Band(_c.Process, 22);
            yield return line;
        }
        if (procs.Count > MaxFaultLines)
        {
            var b = new LineBuilder();
            b.Add("   ", _c.Dim).Add($"{procs.Count - MaxFaultLines} more with {(hard ? "hard" : "soft")} faults", _c.Unit);
            yield return b.Build();
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
            // "C:  busy ⣀⣠⣴⣶⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿  98%"
            // "  └ queued: 3 reads, 1 write"      — drive letter(s), busy time with graph; I/O requests waiting.
            var names = s.Disks.Select(d => Util.DiskNames.Display(d.Label, d.Volumes)).ToList();
            int nameW = Math.Clamp(names.Max(n => n.Length) + 1, 4, 8);
            var lines = new List<Line>();
            for (int i = 0; i < s.Disks.Count; i++)
            {
                var d = s.Disks[i];
                string key = "disk." + d.Label;
                var h = Hist(key);
                h.Add(Math.Clamp(d.ActivePercent, 0, 100));
                bool cond = t.DiskActive.Crossed(d.ActivePercent) || t.DiskQueue.Crossed(d.Queue);
                bool vis = Vis(key, cond);
                pending |= cond || _vis.IsPending(key);
                if (!vis) continue;
                anyBusy = true;
                var b = new LineBuilder();
                b.Add(names[i], _c.Disk).PadTo(nameW);
                b.Add("busy", _c.Unit);
                Graph(b, h.Values, 100, _c.Disk, b.Column + 1, Layout.Width - b.Column - 1 - 1 - Layout.FieldWidth);
                b.Add(" ", _c.Dim);
                Val(b, Format.Pct4(d.ActivePercent), _c.Severity(d.ActivePercent, t.DiskActive.Warn, t.DiskActive.Crit));
                lines.Add(b.Build());

                var q = new LineBuilder();
                var qc = _c.Severity(d.Queue, t.DiskQueue.Warn, t.DiskQueue.Crit);
                q.Add("  └ ", _c.Dim).Add("queued: ", _c.Unit);
                Val(q, Format.QueueText(d.ReadQueue, d.WriteQueue, d.Queue), qc);
                lines.Add(q.Build());
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
                // "C:  free  14GB / 500GB ▮▯▯  3%" — free space of total, bar and percent free.
                var b = new LineBuilder();
                b.Add(v.Root.TrimEnd('\\'), _c.Disk).PadTo(4).Add("free ", _c.Unit);
                lines.Add(AmountRest(b, v.FreeBytes, v.TotalBytes, c, highIsGood: true));
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

    // ───────────── Usage (Claude, Codex, Copilot) ─────────────

    /// <summary>
    /// The usage lines if the source has data. Without data its items are "gone": they and their
    /// section header hide at once, and stay hidden for a minute after a loss (<see cref="DataGate"/>).
    /// </summary>
    private IReadOnlyList<ClaudeLine>? Gate(string source, IReadOnlyList<ClaudeLine>? usage, params string[] ids)
    {
        bool has = usage is { Count: > 0 };
        bool ok = _preview || _gate.Update(source, has, _now);
        if (_trace != null && _gateShown.GetValueOrDefault(source) != ok)
        {
            _trace($"{source} usage: {(ok ? "shown (data)" : has ? "hidden for 1 min after a data loss" : "hidden (no data)")}");
            _gateShown[source] = ok;
        }
        if (ok) return usage;
        foreach (var id in ids) _gone.Add(id);
        return null;
    }

    /// <summary>Usage limits of one provider (Claude, Codex, Copilot): short, long and extra windows to their layout items.</summary>
    private readonly Dictionary<string, bool> _gateShown = new();

    private void ComposeUsage(IReadOnlyList<ClaudeLine>? usage, Dictionary<string, List<Line>> items, string shortId, string longId, string extraId, Rgb accent)
    {
        if (usage is not { Count: > 0 }) return;
        var proj = _cfg.Thresholds.ClaudeProjection;
        bool projection = _cfg.Fields.ClaudeProjection;
        foreach (var c in usage)
        {
            string id = c.ResolvedKind switch { UsageKind.Short => shortId, UsageKind.Long => longId, _ => extraId };
            if (!On(id)) continue;
            if (!items.TryGetValue(id, out var list)) items[id] = list = new();
            list.AddRange(ClaudeLines(c, proj, projection, accent, _usageLabelWidth));
        }
    }

    /// <summary>
    /// "DeathAdder V3 Hyp… ▮▮▮▮▮▯▯▯  67%" — one line per device, lowest battery first in colour.
    /// Shown always, or only when low (Fields.BatteryAlways).
    /// </summary>
    private void ComposeBatteries(Snapshot s, Dictionary<string, List<Line>> items)
    {
        if (!On("devices.battery") || s.Batteries is not { Count: > 0 } list) return;
        var th = _cfg.Thresholds.Battery;
        var lines = new List<Line>();
        const int nameWidth = 16;
        var names = Devices.DeviceNames.Display(list, nameWidth);
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            bool low = th.Crossed(d.Percent);
            if (!_cfg.Fields.BatteryAlways && !Vis("bat." + d.Name, low)) continue;
            // "Razer mouse      ▮▮▮▮▮▮▯▯ ▭ 91%" — name (brand and kind when too long), bar, battery icon, %.
            var c = _c.Severity(d.Percent, th.Warn, th.Crit);
            var b = new LineBuilder();
            b.Add(Format.FitDots(names[i], nameWidth), _c.Device).Add(" ", _c.Dim);
            LevelBar(b, d.Percent / 100.0, 8, highIsGood: true, c);
            b.Add(" ", _c.Dim).Icon(d.Charging == true ? Icons.BatteryCharging : Icons.Battery, d.Charging == true ? _c.Warn : c);
            Val(b, Format.Pct4(d.Percent), c);
            lines.Add(b.Build());
        }
        items["devices.battery"] = lines;
    }

    /// <summary>
    /// Two lines per limit:
    /// "week  :::::::::::::::::::::  71%"
    /// " ↻ 2d22h  ⚠ empty 1d22h early"   (or "◷ will last", "⚠ empty 9h before reset" when it fits)
    /// The label column is <paramref name="labelWidth"/> wide, so bars of all rows have the same length.
    /// </summary>
    public IEnumerable<Line> ClaudeLines(ClaudeLine c, Threshold projection, bool showProjection = true, Rgb? accentColor = null, int labelWidth = Layout.LabelWidth)
    {
        var accent = accentColor ?? _c.Claude;
        var theme = _c.Theme;
        labelWidth = Math.Clamp(labelWidth, 3, 12);
        int barWidth = Layout.Width - labelWidth - 1 - Layout.FieldWidth;
        var b = new LineBuilder();
        b.Add(c.Label.Length >= labelWidth ? c.Label[..(labelWidth - 1)] : c.Label, accent).PadTo(labelWidth);
        var (bar, filled) = Glyphs.StyledBar(c.Used / 100.0, barWidth, theme.BarStyle);
        for (int i = 0; i < bar.Length; i++)
        {
            bool on = i < filled;
            var color = !on ? _c.Dim : theme.BarGradient ? Rgb.Stops(_c.GraphStops, (i + 0.5) / barWidth) : accent;
            b.Add(bar[i].ToString(), color, on && theme.BoldValues ? RunStyle.Bold : RunStyle.None, BarBack);
        }
        b.Add(" ", _c.Dim);
        Val(b, Format.Pct4(c.Used));
        yield return b.Build();

        var r = new LineBuilder();
        r.Add(" ", _c.Dim);
        if (c.ResetIn is { } reset)
        {
            r.Icon(Icons.Refresh, _c.Icon).Add(" ", _c.Dim);
            Val(r, Format.Duration(reset));
        }
        if (showProjection) Projection(r, c.Projection, projection);
        if (showProjection && c.Projection.Kind != ProjectionKind.None)
            r.LineHit(HitAction.None, "", ProjectionTip(c));
        if (r.Column > 1) yield return r.Build();
    }

    private string ProjectionTip(ClaudeLine c)
    {
        var p = c.Projection;
        string reset = Format.When(_now, _now + p.TimeToReset, long_: true);
        string pace = "Pace: " + (c.RecentPace
            ? (c.ResolvedKind == UsageKind.Short ? "the last 30 min" : "the last 24 h")
            : "the average since the window started (not enough own samples yet)");
        if (p.Kind == ProjectionKind.Limit)
            return $"Runs out {Format.When(_now, _now + p.TimeToLimit, long_: true)} at this pace, " +
                   $"{Format.Duration(p.TimeToReset - p.TimeToLimit)} before the reset ({reset}).\n{pace}.";
        return $"Lasts until the reset ({reset}) at this pace.\n{pace}.";
    }

    /// <summary>The projection, right-aligned after the refresh time (short wording when the long one does not fit).</summary>
    private void Projection(LineBuilder r, ProjectionResult p, Threshold projection)
    {
        Run[] runs;
        switch (p.Kind)
        {
            case ProjectionKind.Lasts:
                runs = new[] { new Run(Icons.Clock + " ", _c.Icon, RunStyle.Icon), new Run(" will last", _c.Unit) };
                break;
            case ProjectionKind.Limit:
            {
                // "⚠ empty 1d22h before reset" (or "… early" when it does not fit); the tooltip has the time.
                var color = _c.Severity(p.Ratio, projection.Warn, projection.Crit);
                string d = Format.Duration(p.TimeToReset - p.TimeToLimit);
                runs = new[]
                {
                    new Run(Icons.Warning + " ", color, RunStyle.Icon), new Run(" empty ", color),
                    new Run(d, color, ValueStyle), new Run(" before reset", color),
                };
                if (r.Column + 2 + LineBuilder.Len(runs) > Layout.Width) runs[3] = new Run(" early", color);
                break;
            }
            default:
                runs = new[] { new Run(Icons.Clock + " ", _c.Dim, RunStyle.Icon), new Run(" no trend yet", _c.Dim) };
                break;
        }
        r.Right(runs);
    }
}
