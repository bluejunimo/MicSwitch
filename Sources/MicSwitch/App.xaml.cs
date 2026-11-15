using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MicSwitch.Audio;
using MicSwitch.Services;
using MicSwitch.Settings;
using MicSwitch.Shell;
using MicSwitch.UI;
using MicSwitch.UI.Controls;
using MicSwitch.UI.Pages;
using MicSwitch.UI.ViewModels;
using Wpf.Ui.Appearance;

namespace MicSwitch;

public partial class App
{
    private readonly SingleInstance? instance;
    private SettingsStore? settings;
    private AudioEngine? engine;
    private HotkeyService? hotkeys;
    private UpdateService? updates;
    private TrayIconService? tray;
    private OverlayWindow? overlay;
    private ServiceProvider? services;
    private MainWindow? mainWindow;
    private bool exiting;

    public App()
    {
    }

    internal App(SingleInstance instance)
    {
        this.instance = instance;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled error in the user interface");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Fatal(args.ExceptionObject as Exception, "Unhandled error");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task error");
            args.SetObserved();
        };

        ApplySystemTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        settings = LoadSettings();
        engine = new AudioEngine();
        var state = new AppState(engine, settings, Dispatcher);
        var microphone = new MicrophoneService(engine.Microphone, engine.Player, settings, engine.Queue);
        var speakers = new SpeakerService(engine.Speakers, settings, engine.Queue);
        hotkeys = new HotkeyService(settings, engine.Queue, microphone, speakers);
        updates = new UpdateService(settings);

        services = new ServiceCollection()
            .AddSingleton(settings)
            .AddSingleton(engine)
            .AddSingleton(state)
            .AddSingleton(updates)
            .AddSingleton<StartupService>()
            .AddSingleton(new AppLifetime(ExitApp))
            .AddSingleton<MicrophoneViewModel>()
            .AddSingleton<HotkeysViewModel>()
            .AddSingleton<SoundsViewModel>()
            .AddSingleton<OverlayViewModel>()
            .AddSingleton<SpeakersViewModel>()
            .AddSingleton<GeneralViewModel>()
            .AddSingleton<MicrophonePage>()
            .AddSingleton<HotkeysPage>()
            .AddSingleton<SoundsPage>()
            .AddSingleton<OverlayPage>()
            .AddSingleton<SpeakersPage>()
            .AddSingleton<GeneralPage>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider();

        microphone.Start();
        speakers.Start();
        hotkeys.Start();
        HotkeyBox.RecordingChanged += recording => hotkeys.Paused = recording;

        overlay = new OverlayWindow(state, settings);
        tray = new TrayIconService(state, engine.Microphone, ShowMainWindow, ExitApp);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        if (instance != null)
        {
            instance.ShowRequested += () => Dispatcher.BeginInvoke(ShowMainWindow);
        }

        if (!settings.Current.General.StartMinimized)
        {
            ShowMainWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        tray?.Dispose();
        hotkeys?.Dispose();
        updates?.Dispose();
        engine?.Dispose();
        settings?.Dispose();
        services?.Dispose();
        base.OnExit(e);
    }

    // The main window follows the Windows theme by itself while it is open. This covers the tray menu
    // and overlay when it has never been opened.
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Dispatcher.BeginInvoke(ApplySystemTheme);
        }
    }

    private static void ApplySystemTheme()
    {
        // MICSWITCH_THEME forces a theme (used for testing).
        switch (Environment.GetEnvironmentVariable("MICSWITCH_THEME")?.ToLowerInvariant())
        {
            case "dark":
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            case "light":
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }

    private static SettingsStore LoadSettings()
    {
        Directory.CreateDirectory(AppInfo.DataDirectory);
        var saved = SettingsStore.TryRead(AppInfo.SettingsPath);
        if (saved != null)
        {
            return new SettingsStore(AppInfo.SettingsPath, saved);
        }

        // First run: carry over the setup of MicSwitch 1.x if there is one.
        var imported = LegacySettingsImporter.TryImport(
            AppInfo.LegacyConfigPath, AppInfo.LegacySoundsDirectory, AppInfo.UserSoundsDirectory, AppInfo.IconsDirectory);
        return new SettingsStore(AppInfo.SettingsPath, imported ?? new AppSettings(), saveNow: true);
    }

    private void ShowMainWindow()
    {
        if (exiting || services == null)
        {
            return;
        }

        if (mainWindow == null)
        {
            mainWindow = services.GetRequiredService<MainWindow>();
            mainWindow.ExitRequested += ExitApp;
        }

        Log.Debug("Showing the main window (visible: {Visible})", mainWindow.IsVisible);
        mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Activate();
    }

    private void ExitApp()
    {
        if (exiting)
        {
            return;
        }

        exiting = true;
        Log.Information("Exiting");
        overlay?.Close();
        if (mainWindow != null)
        {
            mainWindow.AllowClose = true;
            mainWindow.Close();
        }

        Shutdown();
    }

    // Devices and input hooks can be left in a stale state by sleep and by the lock screen.
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            RefreshAfterInterruption();
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            RefreshAfterInterruption();
        }
    }

    private void RefreshAfterInterruption()
    {
        Log.Information("Refreshing after sleep or unlock");
        engine?.Refresh();
        hotkeys?.Reinstall();
    }
}
