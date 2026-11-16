using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicSwitch.Settings;
using MicSwitch.Shell;

namespace MicSwitch.UI.ViewModels;

internal sealed partial class GeneralViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly SettingsStore settings;
    private readonly StartupService startup;
    private readonly AppLifetime lifetime;
    private bool syncing;

    [ObservableProperty]
    private bool runAtStartup;

    [ObservableProperty]
    private bool runAsAdministrator;

    [ObservableProperty]
    private bool startMinimized;

    [ObservableProperty]
    private bool closeToTray;

    [ObservableProperty]
    private bool checkForUpdates;

    [ObservableProperty]
    private AppTheme theme;

    [ObservableProperty]
    private StartupMicState startupState;

    [ObservableProperty]
    private bool isStartupStateEnabled;

    [ObservableProperty]
    private bool restoreVolume;

    [ObservableProperty]
    private StartupMicState speakerStartupState;

    [ObservableProperty]
    private bool isSpeakerStartupStateEnabled;

    [ObservableProperty]
    private bool restoreSpeakerVolume;

    public GeneralViewModel(AppState state, SettingsStore settings, StartupService startup, UpdatesViewModel updates, AppLifetime lifetime)
    {
        this.state = state;
        this.settings = settings;
        this.startup = startup;
        this.lifetime = lifetime;
        Updates = updates;
        state.SettingsChanged += Sync;
        Sync();
    }

    public string Version => AppInfo.Version;

    public UpdatesViewModel Updates { get; }

    public bool CanRunAsAdministrator => ElevatedStart.IsSupported;

    public IReadOnlyList<Option<AppTheme>> Themes { get; } =
    [
        new(AppTheme.System, "Use system setting"),
        new(AppTheme.Light, "Light"),
        new(AppTheme.Dark, "Dark")
    ];

    public IReadOnlyList<Option<StartupMicState>> StartupStates { get; } =
    [
        new(StartupMicState.Keep, "Keep as is"),
        new(StartupMicState.Mute, "Mute"),
        new(StartupMicState.Unmute, "Unmute")
    ];

    public string UpstreamUrl => AppInfo.UpstreamUrl;

    private void Sync()
    {
        var current = state.Settings.General;
        syncing = true;
        try
        {
            RunAtStartup = startup.IsEnabled;
            RunAsAdministrator = ElevatedStart.IsEnabled(current);
            StartMinimized = current.StartMinimized;
            CloseToTray = current.CloseToTray;
            CheckForUpdates = current.CheckForUpdates;
            Theme = current.Theme;

            var microphone = state.Settings.Microphone;
            StartupState = microphone.StartupState;
            IsStartupStateEnabled = microphone.Mode == MuteMode.Toggle;
            RestoreVolume = microphone.RestoreVolume;

            var speakers = state.Settings.Speakers;
            SpeakerStartupState = speakers.StartupState;
            IsSpeakerStartupStateEnabled = speakers.Mode == MuteMode.Toggle;
            RestoreSpeakerVolume = speakers.RestoreVolume;
        }
        finally
        {
            syncing = false;
        }
    }

    [RelayCommand]
    private void OpenLogs() => Open(AppInfo.LogsDirectory);

    [RelayCommand]
    private void OpenUpstream() => Open(AppInfo.UpstreamUrl);

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not open {Target}", target);
        }
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        if (!syncing)
        {
            startup.SetEnabled(value);
            Sync();
        }
    }

    partial void OnRunAsAdministratorChanged(bool value)
    {
        if (!syncing)
        {
            _ = SetRunAsAdministrator(value);
        }
    }

    private async Task SetRunAsAdministrator(bool value)
    {
        // Off the UI thread: this waits for the Windows administrator prompt.
        var done = await Task.Run(() => value ? ElevatedStart.Register() : ElevatedStart.Unregister());
        if (done)
        {
            settings.Update(s => s.General.ElevatedExecutable = value ? Environment.ProcessPath : null);
        }

        Sync();

        // Turning it on only helps once this copy is replaced by an elevated one.
        if (done && value && !ElevatedStart.IsElevated)
        {
            settings.Flush();
            if (ElevatedStart.Run())
            {
                lifetime.Exit();
            }
        }
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s => s.General.StartMinimized = value);
        }
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s => s.General.CloseToTray = value);
        }
    }

    partial void OnThemeChanged(AppTheme value)
    {
        if (!syncing)
        {
            settings.Update(s => s.General.Theme = value);
        }
    }

    partial void OnStartupStateChanged(StartupMicState value)
    {
        if (!syncing)
        {
            settings.Update(s => s.Microphone.StartupState = value);
        }
    }

    partial void OnRestoreVolumeChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s =>
            {
                s.Microphone.RestoreVolume = value;
                s.Microphone.Volume = value ? state.Microphone.Volume : null;
            });
        }
    }

    partial void OnSpeakerStartupStateChanged(StartupMicState value)
    {
        if (!syncing)
        {
            settings.Update(s => s.Speakers.StartupState = value);
        }
    }

    partial void OnRestoreSpeakerVolumeChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s =>
            {
                s.Speakers.RestoreVolume = value;
                s.Speakers.Volume = value ? state.Speakers.Volume : null;
            });
        }
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s => s.General.CheckForUpdates = value);
        }
    }
}
