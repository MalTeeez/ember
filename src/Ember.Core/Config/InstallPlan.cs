using System.Security;
using System.Text;

namespace Ember.Core.Config;

/// <summary>Pure parts of the install: the scheduled task XML and command-line quoting.</summary>
public static class InstallPlan
{
    public const string TaskName = "Ember";

    /// <summary>
    /// Task "Ember": at logon of <paramref name="user"/>, highest privileges (no UAC prompt),
    /// no time limit, runs on battery, one instance. For "schtasks /Create /XML" (save as UTF-16).
    /// </summary>
    public static string TaskXml(string exePath, string user)
    {
        string exe = SecurityElement.Escape(exePath), u = SecurityElement.Escape(user);
        string dir = SecurityElement.Escape(Path.GetDirectoryName(exePath) ?? "");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Ember desktop widget</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{u}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{u}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <AllowHardTerminate>true</AllowHardTerminate>
                <Priority>7</Priority>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
              </Settings>
              <Actions Context="Author"><Exec><Command>{exe}</Command><WorkingDirectory>{dir}</WorkingDirectory></Exec></Actions>
            </Task>
            """;
    }

    /// <summary>One argument quoted for a Windows command line (CommandLineToArgvW rules).</summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
        var sb = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') sb.Append('\\', slashes * 2 + 1).Append('"');
            else sb.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        sb.Append('\\', slashes * 2).Append('"');
        return sb.ToString();
    }

    public static string Join(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

    /// <summary>What the install button does for the running exe.</summary>
    public enum ButtonKind { Install, Update, Uninstall }

    public static ButtonKind Button(bool installed, string runningExe, string installedExe)
        => !installed ? ButtonKind.Install
         : string.Equals(Path.GetFullPath(runningExe), Path.GetFullPath(installedExe), StringComparison.OrdinalIgnoreCase) ? ButtonKind.Uninstall
         : ButtonKind.Update;

    public static string ButtonText(ButtonKind k) => k switch
    {
        ButtonKind.Install => "Install (start at logon)…",
        ButtonKind.Update => "Update install with this version…",
        _ => "Uninstall…",
    };
}
