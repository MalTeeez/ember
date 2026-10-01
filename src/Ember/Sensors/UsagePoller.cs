using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Ember.App;
using Ember.Core.Claude;

namespace Ember.Sensors;

/// <summary>
/// Polls a usage endpoint (Claude, Codex, Copilot) on a timer. Any failure hides the lines of the
/// provider; HTTP 429 doubles the interval until a request succeeds. Tokens are only read, never refreshed.
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

    protected UsagePoller(TimeSpan baseInterval)
    {
        _backoff = new PollBackoff(baseInterval);
        _timer = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Applies the interval and on/off setting. Polls soon when turned on.</summary>
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
            Fail(Source + ".poll", ex);
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
            _backoff.OnRateLimited();
            Fail(Source + ".429");
            Log.Write($"{Source}: 429, next poll in {_backoff.Current}");
            return;
        }
        if (!resp.IsSuccessStatusCode)
        {
            Fail(Source + ".http." + (int)resp.StatusCode);
            return;
        }
        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = Parse(body, DateTimeOffset.UtcNow);
        if (data == null)
        {
            Fail(Source + ".parse");
            return;
        }
        _backoff.OnSuccess();
        lock (_gate) _tracker.Update(data, DateTimeOffset.UtcNow);
        Log.SourceOk(Source);
    }

    protected void Fail(string source, Exception? ex = null)
    {
        lock (_gate) _tracker.Fail();
        Log.SourceFailed(source, ex);
        Log.SourceFailed(Source);
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
    private readonly string _credentialsPath;
    public ClaudePoller(string credentialsPath) : base(TimeSpan.FromMinutes(1)) => _credentialsPath = credentialsPath;
    protected override string Source => "claude";

    protected override async Task<HttpRequestMessage?> CreateRequestAsync()
    {
        var json = await ReadFileAsync(_credentialsPath).ConfigureAwait(false);
        var token = json == null ? null : OAuthToken.Parse(json);
        if (token == null || token.IsExpired(DateTimeOffset.UtcNow))
        {
            // Do not refresh: Claude Code refreshes the file itself the next time it runs.
            Fail(token == null ? "claude.credentials" : "claude.token.expired");
            return null;
        }
        var req = new HttpRequestMessage(HttpMethod.Get, UsageApi.Url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        req.Headers.TryAddWithoutValidation(UsageApi.BetaHeader, UsageApi.BetaValue);
        return req;
    }

    protected override UsageData? Parse(string body, DateTimeOffset now) => UsageData.Parse(body);
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
            Fail(auth == null ? "codex.auth" : "codex.token.expired");
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
            Fail("copilot.token");
            return null;
        }
        var req = new HttpRequestMessage(HttpMethod.Get, CopilotUsage.Url);
        req.Headers.Authorization = new AuthenticationHeaderValue("token", token);
        req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2025-04-01");
        return req;
    }

    protected override UsageData? Parse(string body, DateTimeOffset now) => CopilotUsage.Parse(body, now);
}
