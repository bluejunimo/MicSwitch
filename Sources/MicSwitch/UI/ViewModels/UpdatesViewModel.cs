using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicSwitch.Shell;

namespace MicSwitch.UI.ViewModels;

/// <summary>
/// The state of updating, for the Updates section in General and for the banner the window shows
/// when a new version is waiting. One button does whatever comes next: check, install, or restart.
/// </summary>
internal sealed partial class UpdatesViewModel : ObservableObject
{
    private readonly UpdateService updates;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private string? dismissedVersion;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string actionText = "Check for updates";

    [ObservableProperty]
    private bool canAct;

    /// <summary>A new version is waiting for a click (to download and install it, or just to restart).</summary>
    [ObservableProperty]
    private bool isUpdateOffered;

    [ObservableProperty]
    private bool isBannerOpen;

    [ObservableProperty]
    private string bannerTitle = string.Empty;

    public UpdatesViewModel(UpdateService updates)
    {
        this.updates = updates;
        updates.StatusChanged += () => dispatcher.BeginInvoke(Sync);
        Sync();
    }

    public bool IsSupported => updates.IsSupported;

    private void Sync()
    {
        var status = updates.Status;
        var version = updates.NewVersion;
        StatusText = !updates.IsSupported
            ? "Only an installed copy can update itself."
            : status switch
            {
                UpdateStatus.Checking => "Checking…",
                UpdateStatus.UpToDate => "You're up to date",
                UpdateStatus.Available => $"Version {version} is available",
                UpdateStatus.Downloading => $"Downloading version {version}… {updates.Progress}%",
                UpdateStatus.ReadyToRestart => $"Version {version} is ready to install",
                UpdateStatus.Failed => "Couldn't reach the update server",
                _ => string.Empty
            };

        IsUpdateOffered = status is UpdateStatus.Available or UpdateStatus.ReadyToRestart;
        ActionText = status switch
        {
            UpdateStatus.Available => "Download and install",
            UpdateStatus.ReadyToRestart => "Restart to update",
            _ => "Check for updates"
        };
        CanAct = updates.IsSupported && status is not (UpdateStatus.Checking or UpdateStatus.Downloading);
        BannerTitle = status == UpdateStatus.ReadyToRestart
            ? $"MicSwitch {version} is ready to install"
            : $"MicSwitch {version} is available";
        IsBannerOpen = IsUpdateOffered && version != dismissedVersion;
    }

    /// <summary>Checks, or installs what an earlier check found.</summary>
    [RelayCommand]
    private Task Act() => IsUpdateOffered ? updates.InstallAsync() : updates.CheckAsync();

    [RelayCommand]
    private void DismissBanner()
    {
        dismissedVersion = updates.NewVersion;
        IsBannerOpen = false;
    }
}
