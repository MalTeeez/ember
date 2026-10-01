using System.Text.RegularExpressions;

namespace Ember.Core.Util;

/// <summary>Process entry for tree operations.</summary>
public readonly record struct ProcEntry(int Pid, int ParentPid, long CreateTime);

public static class ProcessTree
{
    /// <summary>
    /// All descendants of <paramref name="root"/> (children first in depth order, root not included).
    /// A child must be created after its parent; this stops errors from reused parent PIDs.
    /// </summary>
    public static List<int> Descendants(int root, IReadOnlyCollection<ProcEntry> all)
    {
        var byPid = all.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
        var children = all.Where(p => p.Pid != p.ParentPid).ToLookup(p => p.ParentPid);
        var result = new List<int>();
        var seen = new HashSet<int> { root };
        var stack = new Stack<int>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            int pid = stack.Pop();
            long parentCreate = byPid.TryGetValue(pid, out var pe) ? pe.CreateTime : long.MinValue;
            foreach (var c in children[pid])
            {
                if (c.CreateTime < parentCreate) continue;
                if (!seen.Add(c.Pid)) continue;
                result.Add(c.Pid);
                stack.Push(c.Pid);
            }
        }
        return result;
    }
}

public static partial class DiskNames
{
    [GeneratedRegex(@"^(\d+)\s*(.*)$")]
    private static partial Regex PhysicalRegex();

    /// <summary>PDH PhysicalDisk instance "0 C: D:" → label "d0", volumes "C: D:". _Total gives null.</summary>
    public static (string Label, string Volumes)? Parse(string instance)
    {
        if (instance.StartsWith('_')) return null;
        var m = PhysicalRegex().Match(instance.Trim());
        if (!m.Success) return null;
        return ("d" + m.Groups[1].Value, m.Groups[2].Value.Trim());
    }

    /// <summary>
    /// Readable name of a physical disk: its drive letter ("C:"); with more partitions up to 3 letters
    /// ("C:D:", "C:D:E:"); without a letter "disk0".
    /// </summary>
    public static string Display(string label, string volumes)
    {
        var letters = volumes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.TrimEnd(':', '\\')).Where(v => v.Length == 1 && char.IsLetter(v[0])).ToList();
        if (letters.Count == 0) return "disk" + label.TrimStart('d');
        return string.Concat(letters.Take(3).Select(l => char.ToUpperInvariant(l[0]) + ":"));
    }
}

public static class CpuNames
{
    /// <summary>
    /// PDH "Processor Information" instance "g,n" → (group, number); "_Total" and "g,_Total" give null.
    /// </summary>
    public static (int Group, int Number)? Parse(string instance)
    {
        var parts = instance.Split(',');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], out int g) || !int.TryParse(parts[1], out int n)) return null;
        return (g, n);
    }
}

/// <summary>Short process names for the widget.</summary>
public static class ProcessNames
{
    /// <summary>"chrome.exe" → "chrome" (every Windows program ends in .exe).</summary>
    public static string StripExe(string name)
        => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    /// <summary>
    /// Name plus the first real argument of the command line: "python3.13 main.py", "node server.js".
    /// The argument is the first one that is not an option (-x, --x, /x); a path gives its file name.
    /// Without a command line or such an argument: the name only.
    /// </summary>
    public static string Display(string name, string? commandLine)
    {
        string baseName = StripExe(name);
        var arg = FirstArgument(commandLine);
        return arg == null ? baseName : baseName + " " + arg;
    }

    public static string? FirstArgument(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var args = Split(commandLine);
        foreach (var a in args.Skip(1))
        {
            if (a.Length == 0 || a[0] is '-' or '/') continue;
            string t = a.TrimEnd('\\', '/');
            int slash = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
            if (slash >= 0) t = t[(slash + 1)..];
            return t.Length > 0 ? t : null;
        }
        return null;
    }

    /// <summary>Splits a Windows command line at spaces outside double quotes (quotes removed).</summary>
    public static List<string> Split(string commandLine)
    {
        var list = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in commandLine)
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any || cur.Length > 0) list.Add(cur.ToString());
                cur.Clear();
                any = false;
                continue;
            }
            cur.Append(c);
        }
        if (any || cur.Length > 0) list.Add(cur.ToString());
        return list;
    }
}
