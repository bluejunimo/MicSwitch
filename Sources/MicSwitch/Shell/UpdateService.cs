using MicSwitch.Settings;
using Velopack;
using Velopack.Sources;

namespace MicSwitch.Shell;

internal enum UpdateStatus
{
    Idle,
    Checking,
    UpToDate,

    /// <summary>A newer version exists but has not been downloaded (updates are set to manual).</summary>
    Available,
    Downloading,

    /// <summary>Downloaded; a restart installs it.</summary>
    ReadyToRestart,
    Failed
}

/// <summary>
/// Updates from the GitHub releases of <see cref="AppInfo.RepositoryUrl"/>, without the user visiting GitHub.
/// With automatic updates on, new versions are found and downloaded in the background; installing is a
/// restart (one click, or simply the next start). With them off nothing happens until the user checks.
/// Only works in an installed copy; a build run from the project folder has nothing to update.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly SettingsStore settings;
    private readonly UpdateManager? manager;
    private readonly Timer? timer;
    private UpdateInfo? found;
    private UpdateInfo? downloaded;
    private int busy;

    public UpdateService(SettingsStore settings)
    {
        this.settings = settings;
        if (string.IsNullOrEmpty(AppInfo.UpdateSource))
        {
            return;
        }

        try
        {
            // A folder or plain URL is used as it is (testing); otherwise it is a GitHub repository.
            var candidate = AppInfo.UpdateSource == AppInfo.RepositoryUrl
                ? new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, null, false))
                : new UpdateManager(AppInfo.UpdateSource);
            if (candidate.IsInstalled)
            {
                manager = candidate;
                timer = new Timer(_ => _ = AutoCheckAsync(), null, FirstCheckDelay, CheckInterval);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Updates are unavailable");
        }
    }

    /// <summary>False when no repository is configured or this copy wasn't installed by the installer.</summary>
    public bool IsSupported => manager != null;

    public UpdateStatus Status { get; private set; }

    public string? NewVersion { get; private set; }

    /// <summary>Download progress, 0..100.</summary>
    public int Progress { get; private set; }

    /// <summary>True when the current status came from a background check rather than from the user asking.</summary>
    public bool FoundInBackground { get; private set; }

    /// <summary>Raised on an arbitrary thread.</summary>
    public event Action? StatusChanged;

    /// <summary>The user asked. Downloads what it finds only when updates are automatic.</summary>
    public Task CheckAsync() => CheckAsync(download: settings.Current.General.CheckForUpdates, background: false);

    /// <summary>One click: downloads the update if that is still to do, then restarts into it.</summary>
    public async Task InstallAsync()
    {
        if (manager == null)
        {
            return;
        }

        if (Status == UpdateStatus.Available && found is { } update && Interlocked.Exchange(ref busy, 1) == 0)
        {
            try
            {
                await DownloadAsync(update).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Update download failed");
                SetStatus(UpdateStatus.Failed);
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        }

        if (Status == UpdateStatus.ReadyToRestart && downloaded != null)
        {
            Log.Information("Restarting to install version {Version}", NewVersion);
            manager.ApplyUpdatesAndRestart(downloaded);
        }
    }

    public void Dispose() => timer?.Dispose();

    private Task AutoCheckAsync() =>
        settings.Current.General.CheckForUpdates ? CheckAsync(download: true, background: true) : Task.CompletedTask;

    private async Task CheckAsync(bool download, bool background)
    {
        if (manager == null || Status is UpdateStatus.ReadyToRestart or UpdateStatus.Downloading || Interlocked.Exchange(ref busy, 1) == 1)
        {
            return;
        }

        try
        {
            FoundInBackground = background;
            if (!background)
            {
                SetStatus(UpdateStatus.Checking);
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update == null)
            {
                SetStatus(UpdateStatus.UpToDate);
                return;
            }

            found = update;
            NewVersion = update.TargetFullRelease.Version.ToString();
            Log.Information("Version {Version} is available", NewVersion);
            if (download)
            {
                await DownloadAsync(update).ConfigureAwait(false);
            }
            else
            {
                SetStatus(UpdateStatus.Available);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
            SetStatus(background ? UpdateStatus.Idle : UpdateStatus.Failed);
        }
        finally
        {
            Interlocked.Exchange(ref busy, 0);
        }
    }

    private async Task DownloadAsync(UpdateInfo update)
    {
        Progress = 0;
        SetStatus(UpdateStatus.Downloading);
        await manager!.DownloadUpdatesAsync(update, percent =>
        {
            if (percent != Progress)
            {
                Progress = percent;
                StatusChanged?.Invoke();
            }
        }).ConfigureAwait(false);
        downloaded = update;
        SetStatus(UpdateStatus.ReadyToRestart);
    }

    private void SetStatus(UpdateStatus status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }
}
