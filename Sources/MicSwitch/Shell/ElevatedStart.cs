using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using MicSwitch.Settings;

namespace MicSwitch.Shell;

/// <summary>
/// Runs MicSwitch as administrator without a prompt at every start. Windows hides input meant for an
/// administrator's window from ordinary apps, and only a scheduled task can start an elevated app silently.
/// The task is registered once (the only prompt); from then on an ordinary start hands over to it.
/// </summary>
internal static class ElevatedStart
{
    // Not plain "MicSwitch": MicSwitch 1.x keeps its own startup task under that name.
    public const string TaskName = AppInfo.Name + " Elevated";

    /// <summary>Marks a copy started by the task, so it never hands over again.</summary>
    public const string Argument = "--elevated";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static string Schtasks => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    public static bool IsElevated => Environment.IsPrivilegedProcess;

    /// <summary>The task can't tell a copy where its own data directory (testing) is.</summary>
    public static bool IsSupported => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MICSWITCH_DATA_DIR"));

    /// <summary>The task starts one executable, so the setting only counts for the copy it was made for.</summary>
    public static bool IsEnabled(GeneralSettings? general) =>
        IsSupported && string.Equals(general?.ElevatedExecutable, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);

    public static bool StartedByTask(string[] args) => args.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when this ordinary start should let the task start an elevated copy instead.</summary>
    public static bool ShouldHandOver(string[] args) =>
        !IsElevated && !StartedByTask(args) && IsEnabled(SettingsStore.TryRead(AppInfo.SettingsPath)?.General);

    /// <summary>Creates the task. Shows the Windows administrator prompt unless already elevated.</summary>
    public static bool Register()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var xml = BuildTaskXml(Environment.ProcessPath!, Argument, identity.User!.Value, highestPrivileges: true);
        return Create(TaskName, xml, elevate: !IsElevated);
    }

    /// <summary>Removes the task. False only when the administrator prompt was declined.</summary>
    public static bool Unregister() => !Exists(TaskName) || Invoke(!IsElevated, "/delete", "/tn", TaskName, "/f") != null;

    /// <summary>Starts the elevated copy.</summary>
    public static bool Run() => Run(TaskName);

    internal static bool Create(string name, string xml, bool elevate)
    {
        var file = Path.Combine(Path.GetTempPath(), $"MicSwitch-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            return Invoke(elevate, "/create", "/tn", name, "/xml", file, "/f") == 0;
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "Could not write the task definition");
            return false;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    internal static bool Run(string name) => Invoke(false, "/run", "/tn", name) == 0;

    internal static bool Exists(string name) => Invoke(false, "/query", "/tn", name) == 0;

    internal static bool Delete(string name, bool elevate) => Invoke(elevate, "/delete", "/tn", name, "/f") == 0;

    internal static string BuildTaskXml(string command, string arguments, string userSid, bool highestPrivileges)
    {
        var exec = new XElement(Ns + "Exec", new XElement(Ns + "Command", command));
        if (arguments.Length > 0)
        {
            exec.Add(new XElement(Ns + "Arguments", arguments));
        }

        var task = new XElement(
            Ns + "Task",
            new XAttribute("version", "1.2"),
            new XElement(
                Ns + "RegistrationInfo",
                new XElement(Ns + "Description", "Starts MicSwitch as administrator so its hotkeys work in every app.")),
            new XElement(
                Ns + "Principals",
                new XElement(
                    Ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", userSid),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", highestPrivileges ? "HighestAvailable" : "LeastPrivilege"))),
            new XElement(
                Ns + "Settings",

                // A second start while one copy runs must still launch, so it can bring up the window.
                new XElement(Ns + "MultipleInstancesPolicy", "Parallel"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "AllowHardTerminate", "false"),
                new XElement(Ns + "AllowStartOnDemand", "true"),
                new XElement(Ns + "Enabled", "true"),

                // Tasks are stopped after three days and run below normal priority unless told otherwise.
                new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                new XElement(Ns + "Priority", "4")),
            new XElement(Ns + "Actions", new XAttribute("Context", "Author"), exec));

        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine + task;
    }

    /// <summary>Runs schtasks.exe and returns its exit code, or null when it could not be started (prompt declined).</summary>
    private static int? Invoke(bool elevate, params string[] arguments)
    {
        var start = new ProcessStartInfo(Schtasks)
        {
            UseShellExecute = elevate,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (elevate)
        {
            start.Verb = "runas";
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process == null || !process.WaitForExit(30_000))
            {
                Log.Warning("Task Scheduler did not answer ({Action})", arguments[0]);
                return null;
            }

            return process.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Warning("Could not run Task Scheduler ({Action}): {Message}", arguments[0], ex.Message);
            return null;
        }
    }
}
