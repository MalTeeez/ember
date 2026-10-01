using System.Diagnostics;

namespace Ember.App;

/// <summary>
/// Start at logon = a scheduled task "Ember" (at logon, highest privileges, no time limit).
/// Reading the state needs no admin rights; a change starts an elevated PowerShell (one UAC prompt).
/// </summary>
internal static class Autostart
{
    public const string TaskName = "Ember";

    public static bool IsEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(10000)) return false;
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Write("autostart query failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>The PowerShell script that creates the task (same as in install.ps1).</summary>
    public static string CreateScript(string exePath, string user)
    {
        string exe = exePath.Replace("'", "''");
        string u = user.Replace("'", "''");
        return string.Join("; ",
            $"$a = New-ScheduledTaskAction -Execute '{exe}'",
            $"$t = New-ScheduledTaskTrigger -AtLogOn -User '{u}'",
            $"$p = New-ScheduledTaskPrincipal -UserId '{u}' -LogonType Interactive -RunLevel Highest",
            "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew",
            $"Register-ScheduledTask -TaskName '{TaskName}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null");
    }

    public static string DeleteScript()
        => $"Unregister-ScheduledTask -TaskName '{TaskName}' -Confirm:$false -ErrorAction SilentlyContinue";

    /// <summary>Runs the change elevated and waits for it (call on a background thread). False if cancelled or failed.</summary>
    public static bool Set(bool enable)
    {
        try
        {
            string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Ember.exe");
            string user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            string script = enable ? CreateScript(exe, user) : DeleteScript();
            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(120000);
            Log.Write($"autostart set {enable}: exit {p.ExitCode}");
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Write("autostart change cancelled: " + ex.Message); // UAC prompt declined
            return false;
        }
        catch (Exception ex)
        {
            Log.Write("autostart change failed: " + ex.Message);
            return false;
        }
    }
}
