using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Ember.Core.Config;
using Ember.Native;

namespace Ember.App;

/// <summary>
/// Install and uninstall from the UI, without scripts.
/// Install = copy the exe to %LOCALAPPDATA%\Ember and create the scheduled task "Ember" (at logon,
/// highest privileges, so later starts need no UAC prompt). Only the change asks for admin: the app
/// starts itself elevated with "--install" or "--uninstall" (one UAC prompt). That helper starts a
/// second helper ("--after-exit"), which waits until this instance has quit, then starts the installed
/// Ember (install) or deletes the install folder (uninstall).
/// </summary>
internal static class Installer
{
    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ember");
    public static string InstalledExe => Path.Combine(InstallDir, "Ember.exe");
    private static string RunningExe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Ember.exe");
    private static string User => $"{Environment.UserDomainName}\\{Environment.UserName}";

    public enum Result { Done, Cancelled, Failed }

    /// <summary>True if the task exists (no admin needed to ask).</summary>
    public static bool TaskExists() => Run("schtasks.exe", "/Query /TN " + InstallPlan.TaskName, out _) == 0;

    public static InstallPlan.ButtonKind CurrentButton()
        => InstallPlan.Button(TaskExists() && File.Exists(InstalledExe), RunningExe, InstalledExe);

    // ───────── Called by the UI (background thread) ─────────

    public static Result Install() => Elevated(new[] { "--install", RunningExe, InstallDir, User, Environment.ProcessId.ToString() });

    public static Result Uninstall(bool removeData)
        => Elevated(new[] { "--uninstall", InstallDir, removeData ? Paths.AppData : "-", Environment.ProcessId.ToString() });

    private static Result Elevated(string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(RunningExe, InstallPlan.Join(args))
            {
                UseShellExecute = true,
                Verb = IsElevated() ? "open" : "runas",
            };
            using var p = Process.Start(psi);
            if (p == null) return Result.Failed;
            if (!p.WaitForExit(120_000)) return Result.Failed;
            Log.Write($"installer {args[0]}: exit {p.ExitCode}");
            return p.ExitCode == 0 ? Result.Done : Result.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Write("installer: UAC prompt declined");
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            Log.Write("installer failed: " + ex.Message);
            return Result.Failed;
        }
    }

    /// <summary>
    /// A second start from another exe while the installed Ember runs (a downloaded new version):
    /// offers to update the install. True if the update ran.
    /// </summary>
    public static bool OfferUpdate()
    {
        try
        {
            if (InstallPlan.Button(File.Exists(InstalledExe), RunningExe, InstalledExe) != InstallPlan.ButtonKind.Update || !TaskExists()) return false;
            var version = typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "?";
            if (W.MessageBox(IntPtr.Zero, $"Ember is already installed and running.\n\nUpdate the installed Ember to this version ({version})?",
                    "Ember", W.MB_YESNO | W.MB_ICONINFORMATION) != W.IDYES) return false;
            var r = Install();
            if (r == Result.Failed) Message("The update did not work. Turn on the debug log for details.", W.MB_ICONWARNING);
            return r == Result.Done;
        }
        catch (Exception ex)
        {
            Log.Write("update offer failed: " + ex.Message);
            return false;
        }
    }

    // ───────── Helper modes (Program.Main, before the single-instance check) ─────────

    public static bool IsHelper(string[] args) => args.Length > 0 && args[0] is "--install" or "--uninstall" or "--after-exit";

    public static int RunHelper(string[] args)
    {
        try
        {
            return args[0] switch
            {
                "--install" => HelperInstall(args[1], args[2], args[3], int.Parse(args[4])),
                "--uninstall" => HelperUninstall(args[1], args[2], int.Parse(args[3])),
                "--after-exit" => AfterExit(int.Parse(args[1]), args[2], args.Skip(3).ToArray()),
                _ => 2,
            };
        }
        catch (Exception ex)
        {
            if (args[0] != "--after-exit") Message("Ember could not finish this:\n\n" + ex.Message, W.MB_ICONWARNING);
            return 1;
        }
    }

    private static int HelperInstall(string source, string dir, string user, int callerPid)
    {
        if (!IsElevated()) throw new InvalidOperationException("Admin rights are needed.");
        string target = Path.Combine(dir, "Ember.exe");
        bool same = string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        StopOthers(target, callerPid);
        if (!same)
        {
            Directory.CreateDirectory(dir);
            Retry(() => File.Copy(source, target, true));
        }
        string xml = Path.Combine(Path.GetTempPath(), "ember-task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(xml, InstallPlan.TaskXml(target, user), System.Text.Encoding.Unicode);
            if (Run("schtasks.exe", $"/Create /TN {InstallPlan.TaskName} /XML {InstallPlan.Quote(xml)} /F", out var err) != 0)
                throw new InvalidOperationException("schtasks: " + err.Trim());
        }
        finally { try { File.Delete(xml); } catch (IOException) { } }
        // Start the installed Ember (through the task, so it has admin rights) when the caller has quit.
        Detached(target, new[] { "--after-exit", callerPid.ToString(), "run" });
        return 0;
    }

    private static int HelperUninstall(string dir, string data, int callerPid)
    {
        if (!IsElevated()) throw new InvalidOperationException("Admin rights are needed.");
        Run("schtasks.exe", $"/Delete /TN {InstallPlan.TaskName} /F", out _); // no task: nothing to do
        StopOthers(Path.Combine(dir, "Ember.exe"), callerPid);
        // The folder can hold the running exe: a copy in %TEMP% deletes it after the caller has quit.
        string tmp = Path.Combine(Path.GetTempPath(), "ember-cleanup-" + Guid.NewGuid().ToString("N")[..8] + ".exe");
        File.Copy(RunningExe, tmp, true);
        Detached(tmp, new[] { "--after-exit", callerPid.ToString(), "remove", dir, data });
        return 0;
    }

    private static int AfterExit(int pid, string what, string[] rest)
    {
        try { using var p = Process.GetProcessById(pid); p.WaitForExit(30_000); }
        catch (ArgumentException) { } // already gone
        Thread.Sleep(300);
        if (what == "run")
            return Run("schtasks.exe", $"/Run /TN {InstallPlan.TaskName}", out _);
        if (what == "remove")
        {
            Retry(() => { if (Directory.Exists(rest[0])) Directory.Delete(rest[0], true); });
            if (rest.Length > 1 && rest[1] != "-") Retry(() => { if (Directory.Exists(rest[1])) Directory.Delete(rest[1], true); });
            // This temp copy: deleted at the next restart.
            W.MoveFileEx(RunningExe, null, W.MOVEFILE_DELAY_UNTIL_REBOOT);
        }
        return 0;
    }

    // ───────── Helpers ─────────

    private static void StopOthers(string exe, int keepPid)
    {
        foreach (var p in Process.GetProcessesByName("Ember"))
        {
            using (p)
            {
                if (p.Id == keepPid || p.Id == Environment.ProcessId) continue;
                try
                {
                    if (!string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            }
        }
    }

    private static void Retry(Action a)
    {
        for (int i = 0; ; i++)
        {
            try { a(); return; }
            catch (Exception ex) when (i < 20 && ex is IOException or UnauthorizedAccessException) { Thread.Sleep(500); }
        }
    }

    private static void Detached(string exe, string[] args)
        => Process.Start(new ProcessStartInfo(exe, InstallPlan.Join(args)) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();

    private static int Run(string exe, string args, out string error)
    {
        error = "";
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return -1;
            var errTask = p.StandardError.ReadToEndAsync();
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(30_000)) return -1;
            error = errTask.Result;
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            error = ex.Message;
            return -1;
        }
    }

    public static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception) { return false; }
    }

    private static void Message(string text, uint icon) => W.MessageBox(IntPtr.Zero, text, "Ember", icon);
}
