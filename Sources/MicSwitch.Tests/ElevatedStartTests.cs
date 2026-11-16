using System.Security.Principal;
using System.Xml.Linq;
using MicSwitch.Settings;
using MicSwitch.Shell;

namespace MicSwitch.Tests;

public class ElevatedStartTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void TaskStartsTheExecutableWithHighestPrivilegesForOneUser()
    {
        var xml = ElevatedStart.BuildTaskXml(@"C:\Apps\Tom & Jerry\MicSwitch.exe", ElevatedStart.Argument, "S-1-5-21-1-2-3-1001", true);
        var task = XDocument.Parse(xml).Root!;

        var principal = task.Element(Ns + "Principals")!.Element(Ns + "Principal")!;
        Assert.Equal("S-1-5-21-1-2-3-1001", principal.Element(Ns + "UserId")!.Value);
        Assert.Equal("HighestAvailable", principal.Element(Ns + "RunLevel")!.Value);

        var exec = task.Element(Ns + "Actions")!.Element(Ns + "Exec")!;
        Assert.Equal(@"C:\Apps\Tom & Jerry\MicSwitch.exe", exec.Element(Ns + "Command")!.Value);
        Assert.Equal("--elevated", exec.Element(Ns + "Arguments")!.Value);
    }

    [Fact]
    public void TaskIsNeverStoppedAndAllowsASecondStart()
    {
        var settings = XDocument.Parse(ElevatedStart.BuildTaskXml("app.exe", "", "S-1-5-18", true)).Root!.Element(Ns + "Settings")!;

        Assert.Equal("PT0S", settings.Element(Ns + "ExecutionTimeLimit")!.Value);
        Assert.Equal("Parallel", settings.Element(Ns + "MultipleInstancesPolicy")!.Value);
        Assert.Equal("false", settings.Element(Ns + "StopIfGoingOnBatteries")!.Value);
        Assert.Null(XDocument.Parse(ElevatedStart.BuildTaskXml("app.exe", "", "S-1-5-18", true)).Root!.Element(Ns + "Triggers"));
    }

    [Fact]
    public void SettingOnlyCountsForTheExecutableItWasMadeFor()
    {
        Assert.False(ElevatedStart.IsEnabled(null));
        Assert.False(ElevatedStart.IsEnabled(new GeneralSettings()));
        Assert.False(ElevatedStart.IsEnabled(new GeneralSettings { ElevatedExecutable = @"C:\Elsewhere\MicSwitch.exe" }));

        var own = new GeneralSettings { ElevatedExecutable = Environment.ProcessPath!.ToUpperInvariant() };
        Assert.Equal(ElevatedStart.IsSupported, ElevatedStart.IsEnabled(own));
    }

    [Fact]
    public void OnlyACopyStartedByTheTaskCarriesTheMarker()
    {
        Assert.True(ElevatedStart.StartedByTask(["--ELEVATED"]));
        Assert.False(ElevatedStart.StartedByTask([]));
        Assert.False(ElevatedStart.StartedByTask(["--minimized"]));
    }

    /// <summary>
    /// Registers a real (unprivileged, harmless) task with Windows, runs it and removes it again.
    /// Changes the machine, so it only runs when MICSWITCH_TASK_TEST=1.
    /// </summary>
    [Fact]
    public void WindowsAcceptsAndRunsTheTask()
    {
        if (Environment.GetEnvironmentVariable("MICSWITCH_TASK_TEST") != "1")
        {
            return;
        }

        var name = "MicSwitch-test-" + Guid.NewGuid().ToString("N")[..8];
        var marker = Path.Combine(Path.GetTempPath(), name + ".txt");
        using var identity = WindowsIdentity.GetCurrent();
        var xml = ElevatedStart.BuildTaskXml(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c echo ran> \"{marker}\"", identity.User!.Value, highestPrivileges: false);

        try
        {
            Assert.False(ElevatedStart.Exists(name));
            Assert.True(ElevatedStart.Create(name, xml, elevate: false));
            Assert.True(ElevatedStart.Exists(name));
            Assert.True(ElevatedStart.Run(name));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(marker), TimeSpan.FromSeconds(10)));
        }
        finally
        {
            ElevatedStart.Delete(name, elevate: false);
            File.Delete(marker);
        }

        Assert.False(ElevatedStart.Exists(name));
    }
}
