namespace Ember.Core.Stats;

/// <summary>
/// Show/hide delay for one item. The condition must stay true for the show delay
/// before the item shows, and must stay false for the hide delay before it hides.
/// </summary>
public sealed class Hysteresis
{
    private DateTime? _since;
    public bool Visible { get; private set; }

    public bool Update(bool condition, DateTime now, TimeSpan showDelay, TimeSpan hideDelay)
    {
        if (condition == Visible)
        {
            _since = null; // state agrees with condition: no pending change
            return Visible;
        }
        _since ??= now;
        var needed = Visible ? hideDelay : showDelay;
        if (now - _since.Value >= needed)
        {
            Visible = condition;
            _since = null;
        }
        return Visible;
    }

    public void Reset()
    {
        Visible = false;
        _since = null;
    }

    /// <summary>True while the item is visible or a show is pending.</summary>
    public bool Pending => Visible || _since != null;
}

/// <summary>
/// A set of <see cref="Hysteresis"/> trackers by key. Keys that are not updated in a
/// tick are removed at <see cref="EndTick"/> (silent failure: a missing source hides its item at once).
/// </summary>
public sealed class VisibilitySet
{
    private readonly Dictionary<string, Hysteresis> _items = new();
    private readonly HashSet<string> _touched = new();
    public TimeSpan ShowDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan HideDelay { get; set; } = TimeSpan.FromSeconds(10);

    public void BeginTick() => _touched.Clear();

    public bool Update(string key, bool condition, DateTime now)
    {
        if (!_items.TryGetValue(key, out var h))
        {
            h = new Hysteresis();
            _items[key] = h;
        }
        _touched.Add(key);
        return h.Update(condition, now, ShowDelay, HideDelay);
    }

    /// <summary>True if the key is visible or about to show (used to start sampling early).</summary>
    public bool IsPending(string key) => _items.TryGetValue(key, out var h) && h.Pending;

    public void EndTick()
    {
        if (_items.Count == _touched.Count) return;
        foreach (var key in _items.Keys.Where(k => !_touched.Contains(k)).ToList())
            _items.Remove(key);
    }

    public int Count => _items.Count;
}
