using System.Net;
using System.Net.Http.Headers;
using Ember.App;
using Ember.Core.Claude;

namespace Ember.Sensors;

/// <summary>
/// Polls the Claude Code OAuth usage endpoint (the one behind /usage in Claude Code).
/// Reads the token from %USERPROFILE%\.claude\.credentials.json on each poll and never refreshes it.
/// Expired token or any failed request hides the Claude lines. On HTTP 429 the interval doubles until a request succeeds.
/// </summary>
internal sealed class ClaudePoller : IDisposable
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private readonly object _gate = new();
    private readonly ClaudeTracker _tracker = new();
    private readonly PollBackoff _backoff = new(TimeSpan.FromMinutes(1));
    private readonly Timer _timer;
    private volatile bool _enabled = true, _paused, _busy;
    private readonly string _credentialsPath;

    public ClaudePoller(string credentialsPath)
    {
        _credentialsPath = credentialsPath;
        _timer = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Applies the interval and on/off setting. Polls at once when turned on.</summary>
    public void Configure(bool enabled, double intervalSeconds)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 15, 3600));
        bool changed = interval != _backoff.Base || enabled != _enabled;
        _backoff.Base = interval;
        if (!changed) return;
        _backoff.OnSuccess();
        _enabled = enabled;
        if (!enabled)
        {
            lock (_gate) _tracker.Fail();
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        else Schedule(TimeSpan.FromSeconds(1));
    }

    public void Start() => Schedule(TimeSpan.FromSeconds(2));

    public void Pause()
    {
        _paused = true;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Resume()
    {
        _paused = false;
        if (_enabled) Schedule(TimeSpan.FromSeconds(2));
    }

    private void Schedule(TimeSpan due)
    {
        if (_paused || !_enabled) return;
        _timer.Change(due, Timeout.InfiniteTimeSpan);
    }

    public IReadOnlyList<ClaudeLine>? Lines(DateTimeOffset now)
    {
        lock (_gate)
        {
            var l = _tracker.Lines(now);
            return l.Count == 0 ? null : l;
        }
    }

    private async Task PollAsync()
    {
        if (_busy || _paused || !_enabled) return;
        _busy = true;
        try
        {
            await PollOnceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Fail("claude.poll", ex);
        }
        finally
        {
            _busy = false;
            Schedule(_backoff.Current);
        }
    }

    private async Task PollOnceAsync()
    {
        string json;
        try { json = await File.ReadAllTextAsync(_credentialsPath).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail("claude.credentials", ex);
            return;
        }
        var token = OAuthToken.Parse(json);
        if (token == null || token.IsExpired(DateTimeOffset.UtcNow))
        {
            // Do not refresh: Claude Code refreshes the file itself the next time it runs.
            Fail(token == null ? "claude.credentials.invalid" : "claude.token.expired");
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, UsageApi.Url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Headers.TryAddWithoutValidation(UsageApi.BetaHeader, UsageApi.BetaValue);
        req.Headers.UserAgent.ParseAdd("Ember/" + (typeof(ClaudePoller).Assembly.GetName().Version?.ToString(3) ?? "0"));
        req.Headers.Accept.ParseAdd("application/json");

        using var resp = await Http.SendAsync(req).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _backoff.OnRateLimited();
            Fail("claude.429");
            Log.Write($"claude: 429, next poll in {_backoff.Current}");
            return;
        }
        if (!resp.IsSuccessStatusCode)
        {
            Fail("claude.http." + (int)resp.StatusCode);
            return;
        }
        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = UsageData.Parse(body);
        if (data == null)
        {
            Fail("claude.parse");
            return;
        }
        _backoff.OnSuccess();
        lock (_gate) _tracker.Update(data, DateTimeOffset.UtcNow);
        Log.SourceOk("claude");
    }

    private void Fail(string source, Exception? ex = null)
    {
        lock (_gate) _tracker.Fail();
        Log.SourceFailed(source, ex);
        Log.SourceFailed("claude");
    }

    public void Dispose() => _timer.Dispose();
}
