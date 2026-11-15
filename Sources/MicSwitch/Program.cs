using System.Runtime;
using MicSwitch.Shell;
using Serilog.Events;
using Velopack;

namespace MicSwitch;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Must come first: the installer and updater start the app with special arguments and expect it to exit quickly.
        VelopackApp.Build().Run();

        // A copy started by the elevated task takes over from the one that started it, which is still exiting.
        var startedByTask = ElevatedStart.StartedByTask(args);
        using var instance = SingleInstance.TryAcquire(startedByTask ? TimeSpan.FromSeconds(10) : default);
        if (instance == null)
        {
            SingleInstance.SignalExisting();
            return;
        }

        var handOverFailed = false;
        if (ElevatedStart.ShouldHandOver(args))
        {
            if (ElevatedStart.Run())
            {
                return;
            }

            handOverFailed = true;
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(Environment.GetEnvironmentVariable("MICSWITCH_DEBUG") == "1" ? LogEventLevel.Debug : LogEventLevel.Information)
            .WriteTo.File(
                Path.Combine(AppInfo.LogsDirectory, "micswitch-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 5_000_000,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // The input hook must never stall behind a long garbage collection.
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

        try
        {
            Log.Information("MicSwitch {Version} starting (administrator: {Elevated})", AppInfo.Version, ElevatedStart.IsElevated);
            if (handOverFailed)
            {
                Log.Warning("The task that starts MicSwitch as administrator did not run; continuing without it");
            }

            var app = new App(instance);
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "MicSwitch crashed");
            throw;
        }
        finally
        {
            Log.Information("MicSwitch stopped");
            Log.CloseAndFlush();
        }
    }
}
