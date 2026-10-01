using System.Text.Json;
using Ember.App;
using Ember.Core.Config;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// All notes. One .md file per note in %APPDATA%\Ember\notes\; window positions in notes.json there.
/// </summary>
internal sealed class NotesManager
{
    private sealed class Entry
    {
        public string Id { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
    }

    private readonly Dictionary<string, NoteWindow> _notes = new();
    private Dictionary<string, Entry> _index = new();
    private LayoutSettings _layout;

    public NotesManager(LayoutSettings layout) => _layout = layout;

    private static string FileOf(string id) => Path.Combine(Paths.NotesDir, id + ".md");

    public void LoadAll()
    {
        try
        {
            Directory.CreateDirectory(Paths.NotesDir);
            if (File.Exists(Paths.NotesIndex))
            {
                var list = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(Paths.NotesIndex)) ?? new();
                _index = list.Where(e => e.Id.Length > 0).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
            }
        }
        catch (Exception ex)
        {
            Log.Write("notes index failed: " + ex.Message);
        }
        try
        {
            int n = 0;
            foreach (var file in Directory.EnumerateFiles(Paths.NotesDir, "*.md"))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                string text = File.ReadAllText(file);
                Open(id, text, _index.TryGetValue(id, out var e) ? new RECT(e.X, e.Y, e.X + e.W, e.Y + e.H) : DefaultRect(n++));
            }
        }
        catch (Exception ex)
        {
            Log.Write("notes load failed: " + ex.Message);
        }
    }

    public void New()
    {
        string id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..4];
        var note = Open(id, "", DefaultRect(_notes.Count));
        SaveText(note);
        SaveIndex();
        note.Focus();
    }

    private static RECT DefaultRect(int n)
    {
        var mon = Monitors.Get(0);
        float s = Monitors.Dpi(mon.Handle) / 96f;
        int w = (int)(280 * s), h = (int)(200 * s), step = (int)(24 * s) * (n % 8);
        int x = mon.Work.Left + (mon.Work.Width - w) / 2 + step;
        int y = mon.Work.Top + (mon.Work.Height - h) / 3 + step;
        return new RECT(x, y, x + w, y + h);
    }

    private NoteWindow Open(string id, string text, RECT rect)
    {
        var note = new NoteWindow(id, text, _layout);
        note.Changed += SaveText;
        note.MovedOrSized += _ => SaveIndex();
        note.DeleteRequested += Delete;
        note.NewRequested += New;
        note.Create(KeepOnScreen(rect));
        _notes[id] = note;
        return note;
    }

    /// <summary>Moves a note onto the nearest monitor if its saved position is off screen.</summary>
    private static RECT KeepOnScreen(RECT r)
    {
        var mons = Monitors.All();
        if (mons.Any(m => m.Work.Contains(r.Left + 20, r.Top + 10))) return r;
        var wa = Monitors.Get(0).Work;
        int x = Math.Clamp(r.Left, wa.Left, Math.Max(wa.Left, wa.Right - r.Width));
        int y = Math.Clamp(r.Top, wa.Top, Math.Max(wa.Top, wa.Bottom - r.Height));
        return new RECT(x, y, x + r.Width, y + r.Height);
    }

    private void Delete(NoteWindow note)
    {
        _notes.Remove(note.Id);
        _index.Remove(note.Id);
        note.Destroy();
        try { File.Delete(FileOf(note.Id)); }
        catch (Exception ex) { Log.Write("note delete failed: " + ex.Message); }
        SaveIndex();
    }

    private static void SaveText(NoteWindow note)
    {
        try
        {
            Directory.CreateDirectory(Paths.NotesDir);
            // RichEdit uses '\r' between lines; files use CRLF.
            string text = note.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
            string path = FileOf(note.Id), tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, true);
        }
        catch (Exception ex)
        {
            Log.Write("note save failed: " + ex.Message);
        }
    }

    private void SaveIndex()
    {
        try
        {
            foreach (var n in _notes.Values)
            {
                var b = n.Bounds;
                if (b.Width <= 0) continue;
                _index[n.Id] = new Entry { Id = n.Id, X = b.Left, Y = b.Top, W = b.Width, H = b.Height };
            }
            Directory.CreateDirectory(Paths.NotesDir);
            string tmp = Paths.NotesIndex + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_index.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, Paths.NotesIndex, true);
        }
        catch (Exception ex)
        {
            Log.Write("notes index save failed: " + ex.Message);
        }
    }

    public void ApplyLayout(LayoutSettings layout)
    {
        _layout = layout;
        foreach (var n in _notes.Values) n.ApplyLayout(layout);
    }

    public void Reattach()
    {
        foreach (var n in _notes.Values) n.Reattach();
    }

    public void BringToFront()
    {
        foreach (var n in _notes.Values) n.BringToFront();
    }

    /// <summary>Saves pending changes and closes all note windows (at exit).</summary>
    public void CloseAll()
    {
        foreach (var n in _notes.Values) n.Flush();
        SaveIndex();
        foreach (var n in _notes.Values) n.Destroy();
        _notes.Clear();
    }
}
