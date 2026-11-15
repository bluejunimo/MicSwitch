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
    private readonly UpdateService updates;
    private bool syncing;

    [ObservableProperty]
    private bool runAtStartup;

    [ObservableProperty]
    private bool startMinimized;

    [ObservableProperty]
    private bool closeToTray;

    [ObservableProperty]
    private bool checkForUpdates;

    [ObservableProperty]
    private string updateStatusText = string.Empty;

    [ObservableProperty]
    private bool isUpdateReady;

    [ObservableProperty]
    private bool canCheckForUpdates = true;

    public GeneralViewModel(AppState state, SettingsStore settings, StartupService startup, UpdateService updates)
    {
        this.state = state;
        this.settings = settings;
        this.startup = startup;
        this.updates = updates;
        updates.StatusChanged += () => Application.Current?.Dispatcher.BeginInvoke(SyncUpdateStatus);
        state.SettingsChanged += Sync;
        Sync();
        SyncUpdateStatus();
    }

    public string Version => AppInfo.Version;

    public bool UpdatesSupported => updates.IsSupported;

    public string UpstreamUrl => AppInfo.UpstreamUrl;

    private void Sync()
    {
        var current = state.Settings.General;
        syncing = true;
        try
        {
            RunAtStartup = startup.IsEnabled;
            StartMinimized = current.StartMinimized;
            CloseToTray = current.CloseToTray;
            CheckForUpdates = current.CheckForUpdates;
        }
        finally
        {
            syncing = false;
        }
    }

    private void SyncUpdateStatus()
    {
        UpdateStatusText = updates.Status switch
        {
            UpdateStatus.Checking => "Checking…",
            UpdateStatus.UpToDate => "Up to date",
            UpdateStatus.Downloading => $"Downloading {updates.NewVersion}…",
            UpdateStatus.ReadyToRestart => $"Version {updates.NewVersion} is ready",
            UpdateStatus.Failed => "Couldn't check for updates",
            _ => string.Empty
        };
        IsUpdateReady = updates.Status == UpdateStatus.ReadyToRestart;
        CanCheckForUpdates = updates.Status is not (UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.ReadyToRestart);
    }

    [RelayCommand]
    private Task CheckNow() => updates.CheckAsync();

    [RelayCommand]
    private void RestartToUpdate() => updates.RestartAndApply();

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

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s => s.General.CheckForUpdates = value);
        }
    }
}
