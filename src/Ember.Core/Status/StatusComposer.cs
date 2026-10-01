using Ember.Core.Claude;
using Ember.Core.Config;
using Ember.Core.Gpu;
using Ember.Core.Net;
using Ember.Core.Render;
using Ember.Core.Stats;

namespace Ember.Core.Status;

/// <summary>
/// Turns snapshots into widget lines. Keeps the graph history, the smoothed core loads
/// and the show/hide state of each item. Pure: no Windows APIs.
/// </summary>
public sealed class StatusComposer
{
    private readonly VisibilitySet _vis = new();
    private readonly CoreSmoother _cores = new();
    private readonly Dictionary<string, History> _hist = new();
    public Needs Needs { get; } = new();

    private const int HistorySamples = Layout.GraphChars * 2;
    private const double AdapterTrafficBps = 8_000; // 1 KB/s

    private History Hist(string key)
    {
        if (!_hist.TryGetValue(key, out var h)) _hist[key] = h = new History(HistorySamples);
        return h;
    }

    /// <summary>Clear all state (after a pause, so old samples do not show as a gap).</summary>
    public void Reset()
    {
        _hist.Clear();
        _cores.Reset();
    }

    public List<Line> Compose(Snapshot s, EmberSettings cfg)
    {
        var now = s.Time;
        _vis.ShowDelay = TimeSpan.FromSeconds(cfg.Intervals.ShowDelaySeconds);
        _vis.HideDelay = TimeSpan.FromSeconds(cfg.Intervals.HideDelaySeconds);
        _vis.BeginTick();
        var lines = new List<Line>();
        var usedHist = new HashSet<string>();

        ComposeCpu(s, cfg, now, lines, usedHist);
        ComposeNet(s, cfg, now, lines, usedHist);
        ComposeGpu(s, cfg, now, lines, usedHist);
        ComposeMemory(s, cfg, now, lines);
        ComposeDisk(s, cfg, now, lines);
        ComposeClaude(s, cfg, lines);

        _vis.EndTick();
        foreach (var k in _hist.Keys.Where(k => !usedHist.Contains(k)).ToList()) _hist.Remove(k);
        // Trim a leading blank line.
        if (lines.Count > 0 && lines[0].Text.Trim().Length == 0) lines.RemoveAt(0);
        return lines;
    }

    // ───────────── CPU ─────────────

    private void ComposeCpu(Snapshot s, EmberSettings cfg, DateTime now, List<Line> lines, HashSet<string> usedHist)
    {
        var t = cfg.Thresholds;
        var it = cfg.Items;
        Needs.TopCpu = false;
        if (s.Cpu is not { } cpu) { _cores.Reset(); return; }

        double load = Math.Clamp(cpu.Load, 0, 100);
        bool tempVis = it.CpuTemp && cpu.Temp is { } temp0 && _vis.Update("cpu.temp", t.CpuTemp.Crossed(temp0), now);

        if (it.CpuLoad)
        {
            var h = Hist("cpu");
            usedHist.Add("cpu");
            h.Add(load);
            var b = new LineBuilder();
            b.Dim("cpu").PadTo(Layout.ValueCol).Bright(Format.Percent(load, 5));
            b.PadTo(Layout.FieldCol);
            if (tempVis) AddTemp(b, cpu.Temp!.Value, t.CpuTemp);
            b.PadTo(Layout.GraphCol);
            AddGraph(b, h.Values, 100, null);
            lines.Add(b.Build());
        }
        else if (tempVis)
        {
            var b = new LineBuilder().Dim("cpu").PadTo(Layout.FieldCol);
            AddTemp(b, cpu.Temp!.Value, t.CpuTemp);
            lines.Add(b.Build());
        }

        // Outlier cores
        bool anyOutlier = false;
        if (it.CpuCores && cpu.CoreLoads is { Length: > 0 } raw)
        {
            var smooth = _cores.Update(raw, now);
            var outliers = OutlierStats.Find(smooth, cpu.CoreGroups, t.CoreMinSigma, t.CoreSigmaFactor);
            var outSet = outliers.Select(o => o.Index).ToHashSet();
            var medians = GroupMedians(smooth, cpu.CoreGroups);
            for (int i = 0; i < smooth.Length; i++)
            {
                if (!_vis.Update("core." + i, outSet.Contains(i), now)) continue;
                anyOutlier = true;
                int g = cpu.CoreGroups != null && i < cpu.CoreGroups.Length ? cpu.CoreGroups[i] : 0;
                lines.Add(CoreLine(i, smooth[i], medians.GetValueOrDefault(g)));
            }
        }
        else
        {
            _cores.Reset();
        }

        // Throttling
        if (it.CpuThrottle && cpu.Perf is { } perf)
        {
            bool cond = perf < t.ThrottlePerf && cpu.Load >= t.ThrottleMinLoad;
            if (_vis.Update("cpu.throttle", cond, now))
                lines.Add(new LineBuilder().Dim("  throttled").Right(new Run("perf ", Palette.Dim), new Run(Format.Percent(perf, 4), Palette.Amber)).Build());
        }

        // Top process
        if (it.CpuTopProcess)
        {
            bool cond = cpu.Load >= t.CpuTopProcessLoad || anyOutlier;
            bool vis = _vis.Update("cpu.top", cond, now);
            Needs.TopCpu = cond || _vis.IsPending("cpu.top");
            if (vis && s.TopCpu is { } p) lines.Add(ProcessLine(p));
        }
    }

    private static Dictionary<int, double> GroupMedians(double[] loads, int[]? groups)
    {
        var d = new Dictionary<int, double>();
        foreach (var g in Enumerable.Range(0, loads.Length).GroupBy(i => groups != null && i < groups.Length ? groups[i] : 0))
            d[g.Key] = OutlierStats.Median(g.Select(i => loads[i]).ToList());
        return d;
    }

    /// <summary>"  c7  ███████▌   │         95%" — bar of 20 cells with a median marker.</summary>
    public static Line CoreLine(int index, double load, double median)
    {
        const int barWidth = 20;
        var b = new LineBuilder();
        b.Dim("  c" + index).PadTo(6);
        string bar = Glyphs.Bar(load / 100.0, barWidth);
        int marker = Glyphs.MarkerCell(median / 100.0, barWidth);
        b.Bright(bar[..marker]).Dim("│").Bright(bar[(marker + 1)..]);
        b.PadTo(27).Bright(Format.Percent(load, 5));
        return b.Build();
    }

    // ───────────── Network ─────────────

    private void ComposeNet(Snapshot s, EmberSettings cfg, DateTime now, List<Line> lines, HashSet<string> usedHist)
    {
        if (!cfg.Items.Network || s.Net is not { } net) return;
        double down = net.Sum(a => a.DownBps), up = net.Sum(a => a.UpBps);
        lines.Add(NetLine("↓", down, Hist("net.down"), usedHist, "net.down"));
        lines.Add(NetLine("↑", up, Hist("net.up"), usedHist, "net.up"));

        // Adapter names: only when more than one physical adapter has traffic.
        var active = net.Where(a => a.DownBps + a.UpBps >= AdapterTrafficBps).ToList();
        if (_vis.Update("net.multi", active.Count > 1, now))
        {
            foreach (var a in net.OrderByDescending(a => a.DownBps + a.UpBps))
            {
                var b = new LineBuilder();
                b.Dim("  └ ").Dim(Format.Fit(a.Name, 11)).Dim(" ↓").Bright(Format.RateCompact(a.DownBps).PadLeft(6))
                 .Dim(" ↑").Bright(Format.RateCompact(a.UpBps).PadLeft(6));
                lines.Add(b.Build());
            }
        }

        // Quality (optional)
        if (cfg.Items.NetworkQuality && s.Quality is { } q)
        {
            var t = cfg.Thresholds;
            bool gwLoss = q.HasGateway && t.PacketLoss.Crossed(q.GatewayLoss);
            bool netLoss = t.PacketLoss.Crossed(q.InternetLoss);
            if (_vis.Update("net.loss", gwLoss || netLoss, now))
            {
                double loss = gwLoss ? q.GatewayLoss : q.InternetLoss;
                string where = gwLoss ? "local" : "isp";
                lines.Add(new LineBuilder().Dim("  loss").PadTo(Layout.ValueCol + 4)
                    .Add(Format.Percent(loss, 4), Gradient.Color(loss, t.PacketLoss.Warn, t.PacketLoss.Crit))
                    .Dim("  " + where).Build());
            }
            if (q.GatewayMs is { } gms && _vis.Update("net.gwping", t.GatewayLatency.Crossed(gms), now))
                lines.Add(PingLine("lan", gms, t.GatewayLatency));
            if (q.InternetMs is { } ims && _vis.Update("net.netping", t.InternetLatency.Crossed(ims), now))
                lines.Add(PingLine("isp", ims, t.InternetLatency));
        }
    }

    private static Line PingLine(string where, double ms, Threshold th)
        => new LineBuilder().Dim("  ping").PadTo(Layout.ValueCol + 4)
            .Add((Format.Int(ms) + " ms").PadLeft(7), Gradient.Color(ms, th.Warn, th.Crit))
            .Dim("  " + where).Build();

    private static Line NetLine(string arrow, double bps, History h, HashSet<string> used, string key)
    {
        used.Add(key);
        h.Add(bps);
        double peak = Math.Max(h.Max, bps);
        var unit = Format.UnitFor(peak);
        var b = new LineBuilder();
        b.Dim(arrow).PadTo(Layout.ValueCol).Bright(Format.Rate5(bps, unit)).Dim(" " + unit.Name);
        b.PadTo(Layout.GraphCol);
        AddGraph(b, h.Values, peak <= 0 ? 1 : peak, null);
        b.PadTo(Layout.PeakCol).Dim(" pk" + Format.Peak(peak, unit).PadLeft(3));
        return b.Build();
    }

    // ───────────── GPU ─────────────

    private void ComposeGpu(Snapshot s, EmberSettings cfg, DateTime now, List<Line> lines, HashSet<string> usedHist)
    {
        if (s.Gpus is not { Count: > 0 } gpus) return;
        var t = cfg.Thresholds;
        var it = cfg.Items;
        var block = new List<Line>();
        var labelCount = new Dictionary<string, int>();
        foreach (var g in gpus)
        {
            string key = "gpu." + g.Luid;
            bool loadVis = it.GpuLoad && g.Load is { } l && _vis.Update(key + ".load", l >= t.GpuLoad, now);
            bool tempVis = it.GpuTemp && g.Temp is { } tc && _vis.Update(key + ".temp", t.GpuTemp.Crossed(tc), now);
            double vramPct = g.VramUsedBytes is { } vu && g.VramTotalBytes is { } vt && vt > 0 ? 100 * vu / vt : double.NaN;
            bool vramVis = it.GpuVram && !g.Integrated && !double.IsNaN(vramPct) && _vis.Update(key + ".vram", t.Vram.Crossed(vramPct), now);
            bool topVis = it.GpuTopProcess && g.Load is { } l2 && _vis.Update(key + ".top", l2 >= t.GpuTopProcessLoad, now);

            // Graph history is kept while load is known, so the graph is full when it shows.
            if (g.Load is { } gl)
            {
                Hist(key).Add(Math.Clamp(gl, 0, 100));
                usedHist.Add(key);
            }
            if (!(loadVis || tempVis || vramVis || (topVis && g.Top != null))) continue;

            string label = GpuNames.ShortLabel(g.Name, g.Integrated);
            labelCount[label] = labelCount.GetValueOrDefault(label) + 1;
            if (labelCount[label] > 1) label += labelCount[label];

            var b = new LineBuilder();
            b.Dim(label).PadTo(Layout.ValueCol);
            if (loadVis) b.Bright(Format.Percent(g.Load!.Value, 5));
            b.PadTo(Layout.FieldCol);
            if (tempVis) AddTemp(b, g.Temp!.Value, t.GpuTemp);
            b.PadTo(Layout.GraphCol);
            if (loadVis) AddGraph(b, Hist(key).Values, 100, null);
            block.Add(b.Build());

            if (vramVis)
            {
                var c = Gradient.Color(vramPct, t.Vram.Warn, t.Vram.Crit);
                block.Add(new LineBuilder().Dim("  vram").PadTo(8)
                    .Bright(GiB(g.VramUsedBytes!.Value) + "/" + GiB(g.VramTotalBytes!.Value)).Dim(" GB")
                    .Right(new Run(Format.Percent(vramPct, 4), c)).Build());
            }
            if (topVis && g.Top is { } p) block.Add(ProcessLine(p));
        }
        if (block.Count == 0) return;
        lines.Add(Layout.Blank());
        lines.Add(Layout.Header("GPU"));
        lines.AddRange(block);
    }

    private static string GiB(double bytes) => (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    // ───────────── Memory ─────────────

    private void ComposeMemory(Snapshot s, EmberSettings cfg, DateTime now, List<Line> lines)
    {
        Needs.TopMem = false;
        if (s.Mem is not { } m) return;
        var t = cfg.Thresholds;
        var it = cfg.Items;
        var block = new List<Line>();

        double availPct = m.TotalBytes > 0 ? 100.0 * m.AvailBytes / m.TotalBytes : double.NaN;
        bool availCond = !double.IsNaN(availPct) && t.AvailableRam.Crossed(availPct);
        if (it.MemAvailable && _vis.Update("mem.avail", availCond, now))
        {
            var c = Gradient.Color(availPct, t.AvailableRam.Warn, t.AvailableRam.Crit);
            block.Add(new LineBuilder().Dim("ram").PadTo(Layout.ValueCol)
                .Add(Format.Bytes(m.AvailBytes), c).Dim(" available")
                .Right(new Run(Format.Percent(availPct, 4), c)).Build());
        }

        double commitPct = m.CommitLimitBytes > 0 ? 100.0 * m.CommitBytes / m.CommitLimitBytes : double.NaN;
        bool commitCond = !double.IsNaN(commitPct) && t.Commit.Crossed(commitPct);
        if (it.MemCommit && _vis.Update("mem.commit", commitCond, now))
        {
            var c = Gradient.Color(commitPct, t.Commit.Warn, t.Commit.Crit);
            block.Add(new LineBuilder().Dim("cmt").PadTo(Layout.ValueCol)
                .Bright(GiB(m.CommitBytes) + "/" + GiB(m.CommitLimitBytes)).Dim(" GB commit")
                .Right(new Run(Format.Percent(commitPct, 4), c)).Build());
        }

        bool pfCond = m.PagesInPerSec is { } pf && t.PageFaults.Crossed(pf);
        if (it.MemPageFaults && m.PagesInPerSec != null && _vis.Update("mem.pf", pfCond, now))
        {
            double pf2 = m.PagesInPerSec.Value;
            block.Add(new LineBuilder().Dim("pgin").PadTo(Layout.ValueCol)
                .Add(Format.Int(pf2), Gradient.Color(pf2, t.PageFaults.Warn, t.PageFaults.Crit)).Dim("/s hard faults").Build());
        }

        bool anyMem = block.Count > 0;
        if (it.MemTopProcess)
        {
            Needs.TopMem = anyMem || _vis.IsPending("mem.avail") || _vis.IsPending("mem.commit") || _vis.IsPending("mem.pf");
            if (anyMem && s.TopMem is { } p) block.Add(ProcessLine(p));
        }
        if (block.Count == 0) return;
        lines.Add(Layout.Blank());
        lines.Add(Layout.Header("MEMORY"));
        lines.AddRange(block);
    }

    // ───────────── Disk ─────────────

    private void ComposeDisk(Snapshot s, EmberSettings cfg, DateTime now, List<Line> lines)
    {
        Needs.TopDisk = false;
        var t = cfg.Thresholds;
        var it = cfg.Items;
        var block = new List<Line>();
        bool anyBusy = false, anyBusyPending = false;

        if (it.DiskBusy && s.Disks != null)
        {
            foreach (var d in s.Disks)
            {
                string key = "disk." + d.Label;
                bool cond = t.DiskActive.Crossed(d.ActivePercent) || t.DiskQueue.Crossed(d.Queue);
                bool vis = _vis.Update(key, cond, now);
                anyBusyPending |= cond || _vis.IsPending(key);
                if (!vis) continue;
                anyBusy = true;
                var b = new LineBuilder();
                b.Dim(d.Label).PadTo(Layout.ValueCol)
                 .Add(Format.Percent(d.ActivePercent, 4), Gradient.Color(d.ActivePercent, t.DiskActive.Warn, t.DiskActive.Crit))
                 .Dim("  q ")
                 .Add(d.Queue.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), Gradient.Color(d.Queue, t.DiskQueue.Warn, t.DiskQueue.Crit))
                 .Dim("  " + d.Volumes);
                block.Add(b.Build());
            }
        }

        if (it.DiskFree && s.Volumes != null)
        {
            foreach (var v in s.Volumes)
            {
                if (v.TotalBytes <= 0) continue;
                double pct = 100 * v.FreeBytes / v.TotalBytes;
                double gb = v.FreeBytes / (1024.0 * 1024 * 1024);
                bool lowGb = gb < t.FreeSpaceGb;
                bool cond = t.FreeSpacePercent.Crossed(pct) || lowGb;
                if (!_vis.Update("vol." + v.Root, cond, now)) continue;
                double sev = Math.Max(Gradient.Severity(pct, t.FreeSpacePercent.Warn, t.FreeSpacePercent.Crit), lowGb ? 1 : 0);
                var c = Gradient.Color(sev);
                block.Add(new LineBuilder().Dim(v.Root.TrimEnd('\\')).PadTo(Layout.ValueCol)
                    .Add(Format.Bytes(v.FreeBytes), c).Dim(" free")
                    .Right(new Run(Format.Percent(pct, 4), c)).Build());
            }
        }

        if (it.DiskTopProcess)
        {
            Needs.TopDisk = anyBusyPending;
            if (anyBusy && s.TopDisk is { } p) block.Add(ProcessLine(p));
        }
        if (block.Count == 0) return;
        lines.Add(Layout.Blank());
        lines.Add(Layout.Header("DISK"));
        lines.AddRange(block);
    }

    // ───────────── Claude ─────────────

    private static void ComposeClaude(Snapshot s, EmberSettings cfg, List<Line> lines)
    {
        if (!cfg.Items.Claude || s.Claude is not { Count: > 0 } cl) return;
        lines.Add(Layout.Blank());
        lines.Add(Layout.Header("CLAUDE"));
        foreach (var c in cl) lines.AddRange(ClaudeLines(c, cfg.Thresholds.ClaudeProjection));
    }

    /// <summary>"5h  ████████▍           42%" and "    ↻ 3h10             lasts".</summary>
    public static IEnumerable<Line> ClaudeLines(ClaudeLine c, Threshold projection)
    {
        const int barWidth = 20;
        var b = new LineBuilder();
        b.Dim(c.Label).PadTo(4).Bright(Glyphs.Bar(c.Used / 100.0, barWidth));
        b.PadTo(Layout.Width - 5).Bright(Format.Percent(c.Used, 5));
        yield return b.Build();

        var r = new LineBuilder();
        r.PadTo(4);
        if (c.ResetIn is { } reset) r.Dim("↻ ").Bright(Format.Duration(reset));
        var p = c.Projection;
        Run proj = p.Kind switch
        {
            ProjectionKind.Lasts => new Run("lasts", Palette.Dim),
            ProjectionKind.Limit => new Run("limit " + Format.Duration(p.TimeToLimit),
                Gradient.Color(p.Ratio, projection.Warn, projection.Crit)),
            _ => new Run("—", Palette.Dim),
        };
        r.Right(proj);
        yield return r.Build();
    }

    // ───────────── helpers ─────────────

    private static void AddTemp(LineBuilder b, double temp, Threshold th)
        => b.Add((" " + Format.Int(temp) + "°").PadLeft(5), Gradient.Color(temp, th.Warn, th.Crit));

    /// <summary>Braille graph; each character is coloured with the gradient of its samples (if a threshold is given).</summary>
    private static void AddGraph(LineBuilder b, IReadOnlyList<double> values, double scale, Threshold? th)
    {
        var (text, max) = Glyphs.Graph(values, Layout.GraphChars, scale);
        for (int i = 0; i < text.Length; i++)
        {
            var c = th == null || double.IsNaN(max[i]) ? Palette.Bright : Gradient.Color(max[i], th.Warn, th.Crit);
            b.Add(text[i].ToString(), c);
        }
    }

    public static Line ProcessLine(ProcInfo p)
    {
        var b = new LineBuilder(Layout.Width, ClickKind.Process, p.Pid);
        string value = " " + p.ValueText;
        b.Dim("  └ ").Bright(Format.Fit(p.Name, Layout.Width - 4 - value.Length)).Bright(value);
        return b.Build();
    }
}
