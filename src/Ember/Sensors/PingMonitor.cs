using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Ember.App;
using Ember.Core.Net;

namespace Ember.Sensors;

/// <summary>
/// Optional network quality: ICMP ping to the default gateway and to an internet host every 5 s.
/// Loss = last 20 pings; latency = median of the last 5 replies.
/// </summary>
internal sealed class PingMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly PingStats _gw = new(20), _net = new(20);
    private readonly Timer _timer;
    private volatile bool _enabled, _paused, _busy;
    private IPAddress? _gateway;
    private DateTime _gatewayTime;
    private volatile string _host = "1.1.1.1";
    private const int TimeoutMs = 1000;

    public PingMonitor() => _timer = new Timer(_ => _ = TickAsync(), null, Timeout.Infinite, Timeout.Infinite);

    private volatile bool _trace;
    private DateTime _traceTime = DateTime.MinValue;
    private IReadOnlyList<TraceHop>? _hops;
    private const int MaxHops = 20;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> HostNames = new();

    public void Configure(bool enabled, string host, bool trace = false)
    {
        _trace = trace;
        if (!trace) _hops = null;
        if (host != _host)
        {
            _host = host;
            lock (_gate) _net.Clear();
        }
        if (enabled == _enabled) return;
        _enabled = enabled;
        if (enabled && !_paused) _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        else
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            lock (_gate) { _gw.Clear(); _net.Clear(); }
        }
    }

    public void Pause()
    {
        _paused = true;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Resume()
    {
        _paused = false;
        lock (_gate) { _gw.Clear(); _net.Clear(); }
        if (_enabled) _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
    }

    /// <summary>Null until there are some results (or when off).</summary>
    public NetQuality? Quality()
    {
        if (!_enabled) return null;
        lock (_gate)
        {
            if (_net.Count < 3 && _gw.Count < 3) return null;
            return new NetQuality(_gw.LossPercent, _net.LossPercent, _gw.MedianLatency(5), _net.MedianLatency(5), _gateway != null && _gw.Count > 0,
                _gateway?.ToString() ?? "", _host) { Hops = _trace ? _hops : null };
        }
    }

    private async Task TickAsync()
    {
        if (_busy || _paused || !_enabled) return;
        _busy = true;
        try
        {
            if (_gateway == null || DateTime.UtcNow - _gatewayTime > TimeSpan.FromMinutes(1))
            {
                var gw = FindGateway();
                if (!Equals(gw, _gateway)) lock (_gate) _gw.Clear();
                _gateway = gw;
                _gatewayTime = DateTime.UtcNow;
            }
            var gwTask = _gateway != null ? PingOnce(_gateway.ToString()) : Task.FromResult<double?>(null);
            var netTask = PingOnce(_host);
            await Task.WhenAll(gwTask, netTask).ConfigureAwait(false);
            if (_trace && DateTime.UtcNow - _traceTime > TimeSpan.FromSeconds(30))
            {
                _traceTime = DateTime.UtcNow;
                _hops = await TraceAsync(_host).ConfigureAwait(false);
            }
            lock (_gate)
            {
                if (_gateway != null) _gw.Add(gwTask.Result);
                _net.Add(netTask.Result);
            }
        }
        catch (Exception ex)
        {
            Log.SourceFailed("ping", ex);
        }
        finally { _busy = false; }
    }

    private static async Task<double?> PingOnce(string host)
    {
        try
        {
            using var p = new Ping();
            var r = await p.SendPingAsync(host, TimeoutMs).ConfigureAwait(false);
            return r.Status == IPStatus.Success ? r.RoundtripTime : null;
        }
        catch (PingException) { return null; }
        catch (SocketException) { return null; }
    }

    /// <summary>
    /// Traceroute: one echo request for each TTL 1..20 at the same time; a hop answers with "TTL expired"
    /// (its address), the target with success. Hops after the target are dropped.
    /// </summary>
    private static async Task<IReadOnlyList<TraceHop>> TraceAsync(string host)
    {
        var tasks = Enumerable.Range(1, MaxHops).Select(async ttl =>
        {
            try
            {
                using var p = new Ping();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = await p.SendPingAsync(host, TimeoutMs, new byte[32], new PingOptions(ttl, true)).ConfigureAwait(false);
                bool ok = r.Status is IPStatus.Success or IPStatus.TtlExpired or IPStatus.TimeExceeded;
                // The round trip of a "TTL expired" answer is not always set: measure it.
                double ms = r.Status == IPStatus.Success && r.RoundtripTime > 0 ? r.RoundtripTime : sw.Elapsed.TotalMilliseconds;
                return new TraceHop(ttl, ok ? r.Address?.ToString() : null, ok ? ms : null);
            }
            catch (PingException) { return new TraceHop(ttl, null, null); }
            catch (SocketException) { return new TraceHop(ttl, null, null); }
        }).ToList();
        var hops = await Task.WhenAll(tasks).ConfigureAwait(false);
        string? target = null;
        try { target = (await Dns.GetHostAddressesAsync(host).ConfigureAwait(false)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString(); }
        catch (SocketException) { }
        var list = new List<TraceHop>();
        foreach (var h in hops)
        {
            list.Add(h);
            if (h.Address != null && (h.Address == target || h.Address == host)) break;
        }
        // Reverse DNS names for the tooltips (cached; at most 1 s for all new ones).
        var names = list.Where(h => h.Address != null && !HostNames.ContainsKey(h.Address)).Select(async h =>
        {
            try
            {
                var e = await Dns.GetHostEntryAsync(h.Address!).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                HostNames[h.Address!] = e.HostName != h.Address ? e.HostName : "";
            }
            catch (Exception) { HostNames[h.Address!] = ""; }
        });
        await Task.WhenAll(names).ConfigureAwait(false);
        return list.Select(h => h.Address != null && HostNames.TryGetValue(h.Address, out var n) && n.Length > 0 ? h with { HostName = n } : h).ToList();
    }

    /// <summary>IPv4 default gateway of the first active physical interface.</summary>
    private static IPAddress? FindGateway()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp) continue;
                foreach (var g in ni.GetIPProperties().GatewayAddresses)
                    if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                        return g.Address;
            }
        }
        catch (NetworkInformationException) { }
        return null;
    }

    public void Dispose() => _timer.Dispose();
}
