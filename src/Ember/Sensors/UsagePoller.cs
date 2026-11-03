using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Ember.App;
using Ember.Core.Claude;

namespace Ember.Sensors;

/// <summary>
/// Polls a usage endpoint (Claude, Codex, Copilot) on a timer. Token and auth failures hide the lines of the
/// provider. Network errors, timeouts, 5xx and 429 keep the last data (hidden after <see cref="StaleAfter"/>)
/// and poll less often until a request succeeds. Tokens are only read, never refreshed.
/// </summary>
internal abstract class UsagePoller : IDisposable
{
    protected static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    protected static string UserAgent => "Ember/" + (typeof(UsagePoller).Assembly.GetName().Version?.ToString(3) ?? "0");

    private readonly object _gate = new();
    private readonly UsageTracker _tracker = new();
    private readonly PollBackoff _backoff;
    private readonly Timer _timer;
    private volatile bool _enabled = true, _paused, _busy;
    protected abstract string Source { get; }

    private DateTime _lastPoll = DateTime.MinValue;

    protected UsagePoller(TimeSpan baseInterval, TimeSpan? minInterval = null)
    {
        _backoff = new PollBackoff(baseInterval, minInterval);
        _timer = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Applies the interval and on/off setting. Turned on: polls soon. A new interval counts from the
    /// last poll (no extra request, so changing the interval never causes a 429).
    /// </summary>
    public void Configure(bool enabled, double intervalSeconds)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, _backoff.MinInterval.TotalSeconds, 3600));
        bool intervalChanged = interval != _backoff.Base;
        bool enabledChanged = enabled != _enabled;
        _backoff.Base = interval;
        if (!intervalChanged && !enabledChanged) return;
        _enabled = enabled;
        if (!enabled)
        {
            lock (_gate) _tracker.Fail();
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }
        if (enabledChanged) { _backoff.OnSuccess(); Schedule(TimeSpan.FromSeconds(1)); return; }
        var due = _lastPoll + _backoff.Current - DateTime.UtcNow;
        Schedule(due < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : due);
    }

    public void Start()
    {
        LoadHistory();
        Schedule(TimeSpan.FromSeconds(2));
    }

    // The long-window samples (for the recent pace) are kept in a small file, so a restart does not lose them.
    private string HistoryPath => Path.Combine(Paths.AppData, $"usage-{Source}.txt");
    private bool _historyLoaded;

    private void LoadHistory()
    {
        if (_historyLoaded) return;
        _historyLoaded = true;
        try
        {
            if (File.Exists(HistoryPath))
            {
                var text = File.ReadAllText(HistoryPath);
                lock (_gate) _tracker.ImportHistory(text, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.SourceFailed(Source + ".history", ex); }
    }

    private void SaveHistory(string text)
    {
        try
        {
            Directory.CreateDirectory(Paths.AppData);
            File.WriteAllText(HistoryPath, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.SourceFailed(Source + ".history", ex); }
    }

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

    /// <summary>Last good data older than this is hidden (requests keep failing).</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    /// <summary>The subscription/plan shown next to the provider name, or null when unknown or stale.</summary>
    public string? Plan
    {
        get { lock (_gate) return _tracker.DataTime != default && DateTime.UtcNow - _tracker.DataTime.UtcDateTime <= (StaleAfter + _backoff.Current) ? _tracker.Data?.Plan : null; }
    }

    public IReadOnlyList<ClaudeLine>? Lines(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (now - _tracker.DataTime > StaleAfter + _backoff.Current) return null;
            var l = _tracker.Lines(now);
            return l.Count == 0 ? null : l;
        }
    }

    private async Task PollAsync()
    {
        if (_busy || _paused || !_enabled) return;
        _busy = true;
        _lastPoll = DateTime.UtcNow;
        LoadHistory();
        try
        {
            await PollOnceAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Transient("network", ex);
        }
        catch (Exception ex)
        {
            Fail("poll", ex);
        }
        finally
        {
            _busy = false;
            Schedule(_backoff.Current);
        }
    }

    /// <summary>Builds the request, or null (and calls <see cref="Fail"/>) when no valid token exists.</summary>
    protected abstract Task<HttpRequestMessage?> CreateRequestAsync();

    protected abstract UsageData? Parse(string body, DateTimeOffset now);

    private async Task PollOnceAsync()
    {
        using var req = await CreateRequestAsync().ConfigureAwait(false);
        if (req == null) return;
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.Accept.ParseAdd("application/json");
        using var resp = await Http.SendAsync(req).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Rate limited: keep the last data (reset times are absolute, so it stays correct) and wait longer.
            Transient("HTTP 429");
            return;
        }
        int code = (int)resp.StatusCode;
        if (code >= 500 || code == 408)
        {
            Transient("HTTP " + code);
            return;
        }
        if (!resp.IsSuccessStatusCode)
        {
            Fail("HTTP " + code + (code is 401 or 403 ? " (token not accepted)" : ""));
            return;
        }
        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = Parse(body, DateTimeOffset.UtcNow);
        if (data == null)
        {
            Transient("answer not understood: " + (body.Length > 200 ? body[..200] + "…" : body));
            return;
        }
        _backoff.OnSuccess();
        string history;
        lock (_gate)
        {
            _tracker.Update(data, DateTimeOffset.UtcNow);
            history = _tracker.ExportHistory();
        }
        SaveHistory(history);
        Log.SourceOk(Source);
    }

    /// <summary>Hides the lines and logs the reason (logged again when the reason changes).</summary>
    protected void Fail(string reason, Exception? ex = null)
    {
        lock (_gate) _tracker.Fail();
        Log.SourceFailed(Source, reason + ", lines hidden", ex);
    }

    /// <summary>A failure that may go away: keep the last data, poll less often.</summary>
    private void Transient(string reason, Exception? ex = null)
    {
        _backoff.OnRateLimited();
        Log.SourceFailed(Source, $"{reason}, keeping last data, next poll in {_backoff.Current:m\\:ss}", ex);
    }

    protected static async Task<string?> ReadFileAsync(string path)
    {
        try { return File.Exists(path) ? await File.ReadAllTextAsync(path).ConfigureAwait(false) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Dispose() => _timer.Dispose();
}

/// <summary>Claude Code: %USERPROFILE%\.claude\.credentials.json, /api/oauth/usage.</summary>
internal sealed class ClaudePoller : UsagePoller
{
    private readonly IReadOnlyList<string> _candidates;
    private string? _subscription;
    // At least 2 min: the endpoint answers 429 to every second request at 1 min.
    public ClaudePoller(IReadOnlyList<string> credentialCandidates) : base(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2)) => _candidates = credentialCandidates;
    protected override string Source => "claude";

    protected override async Task<HttpRequestMessage?> CreateRequestAsync()
    {
        // The CLI and the desktop app can keep .credentials.json in different folders: try each.
        OAuthToken? token = null;
        string tried = "";
        foreach (var path in _candidates)
        {
            tried += (tried.Length > 0 ? ", " : "") + path;
            if (await ReadFileAsync(path).ConfigureAwait(false) is { } json && OAuthToken.Parse(json) is { } t) { token = t; break; }
        }
        if (token == null || token.IsExpired(DateTimeOffset.UtcNow))
        {
            // Do not refresh: Claude Code refreshes the file itself the next time it runs.
            Fail(token == null ? "no token in " + tried : "token expired (run Claude Code to refresh it)");
            return null;
        }
        _subscription = token.SubscriptionType;
        var req = new HttpRequestMessage(HttpMethod.Get, UsageApi.Url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Headers.TryAddWithoutValidation(UsageApi.BetaHeader, UsageApi.BetaValue);
        return req;
    }

    protected override UsageData? Parse(string body, DateTimeOffset now)
        => UsageData.Parse(body) is { } d ? d with { Plan = _subscription } : null;
}

/// <summary>OpenAI Codex: %CODEX_HOME%\auth.json (default %USERPROFILE%\.codex\auth.json), chatgpt.com/backend-api/wham/usage.</summary>
internal sealed class CodexPoller : UsagePoller
{
    public CodexPoller() : base(TimeSpan.FromMinutes(1)) { }
    protected override string Source => "codex";

    public static string AuthPath
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            return Path.Combine(home, "auth.json");
        }
    }

    protected override async Task<HttpRequestMessage?> CreateRequestAsync()
    {
        var json = await ReadFileAsync(AuthPath).ConfigureAwait(false);
        var auth = json == null ? null : CodexUsage.ParseAuth(json);
        if (auth == null || auth.ExpiresAt is { } e && e <= DateTimeOffset.UtcNow)
        {
            // Do not refresh: the Codex CLI refreshes auth.json itself.
            Fail(auth == null ? "no Codex auth.json" : "token expired (run Codex to refresh it)");
            return null;
        }
        var req = new HttpRequestMessage(HttpMethod.Get, CodexUsage.Url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        if (!string.IsNullOrEmpty(auth.AccountId)) req.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", auth.AccountId);
        return req;
    }

    protected override UsageData? Parse(string body, DateTimeOffset now) => CodexUsage.Parse(body, now);
}

/// <summary>
/// GitHub Copilot: token from a Copilot editor plugin (github-copilot\apps.json / hosts.json) or the GitHub CLI
/// (`gh auth token`); api.github.com/copilot_internal/user.
/// </summary>
internal sealed class CopilotPoller : UsagePoller
{
    private string? _ghToken;
    private DateTime _ghTokenTime;

    public CopilotPoller() : base(TimeSpan.FromMinutes(5)) { }
    protected override string Source => "copilot";

    private static IEnumerable<string> PluginFiles()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var dir in new[] { Path.Combine(local, "github-copilot"), Path.Combine(home, ".config", "github-copilot") })
            foreach (var f in new[] { "apps.json", "hosts.json" })
                yield return Path.Combine(dir, f);
    }

    private async Task<string?> TokenAsync()
    {
        foreach (var f in PluginFiles())
            if (await ReadFileAsync(f).ConfigureAwait(false) is { } json && CopilotUsage.TokenFromPluginFile(json) is { } t)
                return t;
        // GitHub CLI: asked at most every 30 min (it starts a process).
        if (_ghToken != null && DateTime.UtcNow - _ghTokenTime < TimeSpan.FromMinutes(30)) return _ghToken;
        _ghToken = GhAuthToken();
        _ghTokenTime = DateTime.UtcNow;
        return _ghToken;
    }

    private static string? GhAuthToken()
    {
        try
        {
            var psi = new ProcessStartInfo("gh", "auth token -h github.com")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            string output = p.StandardOutput.ReadToEnd().Trim();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(5000) || p.ExitCode != 0) return null;
            return output.Length > 0 ? output : null;
        }
        catch (Exception) { return null; } // gh not installed
    }

    protected override async Task<HttpRequestMessage?> CreateRequestAsync()
    {
        var token = await TokenAsync().ConfigureAwait(false);
        if (token == null)
        {
            Fail("no Copilot token");
            return null;
        }
        var req = new HttpRequestMessage(HttpMethod.Get, CopilotUsage.Url);
        req.Headers.Authorization = new AuthenticationHeaderValue("token", token);
        req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2025-04-01");
        return req;
    }

    protected override UsageData? Parse(string body, DateTimeOffset now) => CopilotUsage.Parse(body, now);
}
