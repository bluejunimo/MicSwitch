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
    private OverlayWindow? speakerOverlay;
    private ServiceProvider? services;
    private MainWindow? mainWindow;
    private AppTheme? appliedTheme;
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

        settings = LoadSettings();
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        settings.Changed += changed =>
        {
            if (changed.General.Theme != appliedTheme)
            {
                Dispatcher.BeginInvoke(ApplyTheme);
            }
        };

        engine = new AudioEngine();
        UseBuiltInSounds(engine.Sounds, settings);
        var state = new AppState(engine, settings, Dispatcher);
        var microphone = new MuteService(DeviceProfile.Microphone, engine.Microphone, engine.Player, settings, engine.Queue);
        var speakers = new MuteService(DeviceProfile.Speakers, engine.Speakers, engine.SpeakerPlayer, settings, engine.Queue);
        var speakerVolume = new SpeakerService(engine.Speakers, engine.Queue);
        hotkeys = new HotkeyService(settings, engine.Queue, microphone, speakers, speakerVolume);
        updates = new UpdateService(settings);

        services = new ServiceCollection()
            .AddSingleton(settings)
            .AddSingleton(engine)
            .AddSingleton(state)
            .AddSingleton(updates)
            .AddSingleton<StartupService>()
            .AddSingleton(new AppLifetime(ExitApp))
            .AddSingleton<MicrophoneHeaderViewModel>()
            .AddSingleton<SpeakersHeaderViewModel>()
            .AddSingleton<HotkeysViewModel>()
            .AddSingleton<SoundsViewModel>()
            .AddSingleton<OverlayViewModel>()
            .AddSingleton<SpeakerHotkeysViewModel>()
            .AddSingleton<SpeakerSoundsViewModel>()
            .AddSingleton<SpeakerOverlayViewModel>()
            .AddSingleton<UpdatesViewModel>()
            .AddSingleton<GeneralViewModel>()
            .AddSingleton<HotkeysPage>()
            .AddSingleton<SoundsPage>()
            .AddSingleton<OverlayPage>()
            .AddSingleton<SpeakerHotkeysPage>()
            .AddSingleton<SpeakerSoundsPage>()
            .AddSingleton<SpeakerOverlayPage>()
            .AddSingleton<GeneralPage>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider();

        microphone.Start();
        speakers.Start();
        hotkeys.Start();
        HotkeyBox.RecordingChanged += recording => hotkeys.Paused = recording;

        overlay = new OverlayWindow(state, settings, DeviceProfile.Microphone);
        speakerOverlay = new OverlayWindow(state, settings, DeviceProfile.Speakers);

        // WPF makes the first window the application's main window, and the theme code then gives that
        // window the settings window's backdrop: a solid square behind the overlay icon.
        MainWindow = null;
        tray = new TrayIconService(state, engine.Microphone, updates, ShowMainWindow, ExitApp);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        if (instance != null)
        {
            instance.ShowRequested += () => Dispatcher.BeginInvoke(ShowMainWindow);
        }

        // Right after installing, a window that never appears looks like a failed install.
        if (!settings.Current.General.StartMinimized || Program.JustInstalled)
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

    // Windows switched between light and dark (among other things).
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && appliedTheme == AppTheme.System)
        {
            Dispatcher.BeginInvoke(ApplyTheme);
        }
    }

    private void ApplyTheme()
    {
        // MICSWITCH_THEME forces a theme (used for testing).
        var theme = Environment.GetEnvironmentVariable("MICSWITCH_THEME")?.ToLowerInvariant() switch
        {
            "dark" => AppTheme.Dark,
            "light" => AppTheme.Light,
            _ => settings?.Current.General.Theme ?? AppTheme.System
        };

        appliedTheme = settings?.Current.General.Theme ?? AppTheme.System;
        switch (theme)
        {
            case AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }

    // MicSwitch 1.x kept its stock sounds in the user's folder. Where those are the sounds that now ship
    // with the app, use the shipped ones, so they are listed once and can't be deleted by accident.
    private static void UseBuiltInSounds(SoundLibrary sounds, SettingsStore settings)
    {
        var replaced = sounds.ReplaceCopiesOfBuiltIns();
        if (replaced.Count == 0)
        {
            return;
        }

        settings.Update(s =>
        {
            foreach (var chosen in new[] { s.Sounds, s.Speakers.Sounds })
            {
                chosen.MuteSound = replaced.GetValueOrDefault(chosen.MuteSound, chosen.MuteSound);
                chosen.UnmuteSound = replaced.GetValueOrDefault(chosen.UnmuteSound, chosen.UnmuteSound);
            }
        });
        Log.Information("Using the built-in sounds for {Sounds}", replaced.Keys);
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
            MainWindow = mainWindow;
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
        speakerOverlay?.Close();
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
