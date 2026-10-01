using Ember.Core.Claude;
using Ember.Core.Net;

namespace Ember.Core.Status;

/// <summary>
/// Sample values for "show everything" (debug/preview) mode: real data stays, missing parts get
/// plausible moving values so every item can be seen while choosing a theme or layout.
/// </summary>
public static class DemoData
{
    public static Snapshot Fill(Snapshot s)
    {
        double t = s.Time.TimeOfDay.TotalSeconds;
        double Wave(double period, double lo, double hi, double phase = 0)
            => lo + (hi - lo) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * (t / period + phase)));

        var cores = Enumerable.Range(0, 12).Select(i => i == 3 ? Wave(9, 70, 100) : Wave(13, 5, 30, i * 0.13)).ToArray();
        s.Cpu ??= new CpuData { Load = cores.Average() };
        if (s.Cpu.CoreLoads == null) s.Cpu.CoreLoads = cores;
        s.Cpu.Temp ??= Wave(20, 62, 94);
        s.Cpu.Perf ??= Wave(17, 70, 110);
        s.Cpu.ClockMHz ??= Wave(11, 3200, 5100);
        int n = s.Cpu.CoreLoads.Length;
        s.Cpu.CoreClocksMHz ??= Enumerable.Range(0, n).Select(i => Wave(11, 3000, 5200, i * 0.07)).ToArray();
        s.Cpu.CoreTemps ??= Enumerable.Range(0, n).Select(i => Wave(20, 55, 92, i * 0.05)).ToArray();

        if (s.Net is not { Count: > 1 })
            s.Net = new List<AdapterRate>
            {
                new("Ethernet", Wave(15, 2e6, 90e6), Wave(9, 0.3e6, 6e6), Wave(15, 300, 9000), Wave(9, 100, 3000)),
                new("Wi-Fi", Wave(23, 0.1e6, 20e6, 0.3), Wave(7, 0.05e6, 2e6), Wave(23, 20, 2000, 0.3), Wave(7, 10, 800)),
            };
        s.Quality ??= new NetQuality(Wave(31, 0, 12), Wave(29, 0, 15), Wave(13, 4, 40), Wave(17, 30, 180), true);

        var top = Procs(s.Time, "game.exe", "chrome.exe", "code.exe", v => Math.Round(v) + "%", 20, 70);
        if (s.Gpus is not { Count: > 0 })
            s.Gpus = new List<GpuData> { new() { Luid = "demo", Name = "NVIDIA GeForce RTX 4090" } };
        foreach (var g in s.Gpus)
        {
            g.Load ??= Wave(12, 15, 98);
            g.Temp ??= Wave(25, 55, 88);
            g.VramTotalBytes ??= 24.0 * (1 << 30);
            g.VramUsedBytes ??= g.VramTotalBytes * Wave(40, 0.7, 0.99);
            g.Tops ??= top;
        }

        s.Mem ??= new MemData
        {
            TotalBytes = 32UL << 30,
            AvailBytes = (ulong)((32UL << 30) * Wave(30, 0.03, 0.2)),
            CommitBytes = (ulong)((40UL << 30) * Wave(30, 0.8, 0.97)),
            CommitLimitBytes = 40UL << 30,
            PagesInPerSec = Wave(10, 200, 4000),
            SoftFaultsPerSec = Wave(12, 5000, 60000),
        };
        s.Disks ??= new List<DiskData> { new("d0", "C:", Wave(8, 60, 100), Wave(8, 0.5, 4), Wave(8, 0.4, 3), Wave(5, 0, 1)) };
        s.Volumes ??= new List<VolumeData> { new("C:\\", 14.0 * (1 << 30), 500.0 * (1 << 30)) };

        s.TopCpu ??= top;
        if (s.FaultProcs is not { Count: > 0 })
        {
            s.FaultProcs = new List<FaultProc>
            {
                new(4242, "chrome.exe", 1.4 * (1L << 30), Wave(13, 200, 700)),
                new(5150, "code.exe", 0.9 * (1L << 30), Wave(17, 40, 200)),
                new(6060, "MsMpEng.exe", 0.3 * (1L << 30), Wave(11, 5, 60)),
            };
            s.FaultsTotal = s.FaultProcs.Sum(f => f.HardFaultsPerSec);
        }
        s.TopMem ??= Procs(s.Time, "chrome.exe", "code.exe", "teams.exe", v => Math.Round(v / 10, 1) + "G", 10, 40);
        s.TopDisk ??= Procs(s.Time, "MsMpEng.exe", "onedrive.exe", "steam.exe", v => Math.Round(v) + "M/s", 5, 120);

        if (s.Claude is not { Count: > 0 })
        {
            var now = DateTimeOffset.UtcNow;
            s.Claude = new List<ClaudeLine>
            {
                new("5h", 42, TimeSpan.FromMinutes(52), new ProjectionResult(ProjectionKind.Lasts, TimeSpan.FromHours(9), TimeSpan.FromMinutes(52))),
                new("week", 71, TimeSpan.FromHours(70), new ProjectionResult(ProjectionKind.Limit, TimeSpan.FromHours(24), TimeSpan.FromHours(70))),
                new("opus", 85, TimeSpan.FromHours(70), new ProjectionResult(ProjectionKind.Limit, TimeSpan.FromHours(9), TimeSpan.FromHours(70))),
            };
        }
        if (s.Codex is not { Count: > 0 })
            s.Codex = new List<ClaudeLine>
            {
                new("5h", 18, TimeSpan.FromMinutes(200), new ProjectionResult(ProjectionKind.Lasts, TimeSpan.FromHours(20), TimeSpan.FromMinutes(200)), UsageKind.Short),
                new("week", 64, TimeSpan.FromHours(30), new ProjectionResult(ProjectionKind.Limit, TimeSpan.FromHours(20), TimeSpan.FromHours(30)), UsageKind.Long),
            };
        if (s.Copilot is not { Count: > 0 })
            s.Copilot = new List<ClaudeLine>
            {
                new("premium", 57, TimeSpan.FromDays(12), new ProjectionResult(ProjectionKind.Lasts, TimeSpan.FromDays(20), TimeSpan.FromDays(12)), UsageKind.Long),
            };
        if (s.Batteries is not { Count: > 0 })
            s.Batteries = new List<Ember.Core.Devices.DeviceBattery>
            {
                new("DeathAdder V3 HyperSpeed", Math.Round(Wave(60, 8, 90)), false, "razer"),
                new("WH-1000XM5", 64, null, "bluetooth"),
            };
        return s;
    }

    private static List<ProcInfo> Procs(DateTime time, string a, string b, string c, Func<double, string> fmt, double lo, double hi)
    {
        double t = time.TimeOfDay.TotalSeconds;
        var list = new List<(string Name, int Pid, double V)>
        {
            (a, 4242, lo + (hi - lo) * (0.5 + 0.5 * Math.Sin(t / 7))),
            (b, 5151, lo + (hi - lo) * (0.5 + 0.5 * Math.Sin(t / 11 + 2))),
            (c, 6060, lo),
        };
        return list.OrderByDescending(x => x.V).Select(x => new ProcInfo(x.Pid, x.Name, x.V, fmt(x.V))).ToList();
    }
}
