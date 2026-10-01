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

    public void Clear() => _keep.Clear();
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
