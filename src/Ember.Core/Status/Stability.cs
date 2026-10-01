namespace Ember.Core.Status;

/// <summary>
/// Keeps the space of each layout entry: when an entry gets fewer lines, its old height stays
/// (as empty lines) until it was not needed for the hold time. Lines below do not jump up and down.
/// </summary>
public sealed class HeightKeeper
{
    private readonly Dictionary<string, (int Height, DateTime LastNeeded)> _keep = new();

    /// <summary>Returns the height to reserve for the key.</summary>
    public int Reserve(string key, int lines, DateTime now, TimeSpan hold)
    {
        if (hold <= TimeSpan.Zero)
        {
            _keep.Remove(key);
            return lines;
        }
        if (!_keep.TryGetValue(key, out var k) || lines >= k.Height)
        {
            _keep[key] = (lines, now);
            return lines;
        }
        if (now - k.LastNeeded >= hold)
        {
            _keep[key] = (lines, now);
            return lines;
        }
        return k.Height;
    }

    /// <summary>Drops the kept height of one key (its item is gone on purpose, not just smaller).</summary>
    public void Forget(string key) => _keep.Remove(key);

    public void Clear() => _keep.Clear();
}

/// <summary>
/// Hides a section whose data source stopped giving data, for at least <see cref="HideFor"/>, so a
/// flaky source does not make the section come and go. A source that never had data is just empty.
/// </summary>
public sealed class DataGate
{
    private readonly Dictionary<string, (bool HadData, DateTime LostAt)> _state = new();
    public TimeSpan HideFor { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>True if the section may show its data now.</summary>
    public bool Update(string key, bool hasData, DateTime now)
    {
        _state.TryGetValue(key, out var st);
        if (!hasData)
        {
            if (st.HadData || st.LostAt == default) _state[key] = (false, now);
            return false;
        }
        if (st.LostAt != default && !st.HadData && now - st.LostAt < HideFor && _everHad.Contains(key)) return false;
        _state[key] = (true, default);
        _everHad.Add(key);
        return true;
    }

    private readonly HashSet<string> _everHad = new();

    public void Clear()
    {
        _state.Clear();
        _everHad.Clear();
    }
}

/// <summary>
/// Keeps one top process on screen: a new process replaces the shown one only when it has been
/// the top process for the stick time, or when the shown one is gone from the candidates.
/// </summary>
public sealed class StickyTop
{
    private ProcInfo? _current;
    private int _challenger;
    private DateTime _challengerSince;

    /// <param name="candidates">Processes sorted by value, highest first (may be null or empty).</param>
    public ProcInfo? Update(IReadOnlyList<ProcInfo>? candidates, DateTime now, TimeSpan stick)
    {
        if (candidates == null || candidates.Count == 0)
        {
            _current = null;
            _challenger = 0;
            return null;
        }
        var best = candidates[0];
        var cur = _current == null ? null : candidates.FirstOrDefault(c => c.Pid == _current.Pid);
        if (cur == null || best.Pid == cur.Pid)
        {
            _current = best;
            _challenger = 0;
            return _current;
        }
        if (_challenger != best.Pid)
        {
            _challenger = best.Pid;
            _challengerSince = now;
        }
        if (now - _challengerSince >= stick)
        {
            _current = best;
            _challenger = 0;
        }
        else _current = cur; // keep the shown process, with its new value
        return _current;
    }

    public void Reset()
    {
        _current = null;
        _challenger = 0;
    }
}
