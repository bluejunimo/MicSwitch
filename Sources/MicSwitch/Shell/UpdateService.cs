using MicSwitch.Settings;
using Velopack;
using Velopack.Sources;

namespace MicSwitch.Shell;

internal enum UpdateStatus
{
    Idle,
    Checking,
    UpToDate,
    Downloading,
    ReadyToRestart,
    Failed
}

/// <summary>
/// Updates from the GitHub releases of <see cref="AppInfo.RepositoryUrl"/>. Only does anything in an
/// installed copy; a build run from the project folder has nothing to update.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly SettingsStore settings;
    private readonly UpdateManager? manager;
    private readonly Timer? timer;
    private UpdateInfo? pending;
    private int busy;

    public UpdateService(SettingsStore settings)
    {
        this.settings = settings;
        if (string.IsNullOrEmpty(AppInfo.RepositoryUrl))
        {
            return;
        }

        try
        {
            var candidate = new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, null, false));
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

    /// <summary>Raised on an arbitrary thread.</summary>
    public event Action? StatusChanged;

    public async Task CheckAsync()
    {
        if (manager == null || Status == UpdateStatus.ReadyToRestart || Interlocked.Exchange(ref busy, 1) == 1)
        {
            return;
        }

        try
        {
            SetStatus(UpdateStatus.Checking);
            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update == null)
            {
                SetStatus(UpdateStatus.UpToDate);
                return;
            }

            NewVersion = update.TargetFullRelease.Version.ToString();
            SetStatus(UpdateStatus.Downloading);
            await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            pending = update;
            SetStatus(UpdateStatus.ReadyToRestart);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
            SetStatus(UpdateStatus.Failed);
        }
        finally
        {
            Interlocked.Exchange(ref busy, 0);
        }
    }

    public void RestartAndApply()
    {
        if (manager != null && pending != null)
        {
            manager.ApplyUpdatesAndRestart(pending);
        }
    }

    public void Dispose() => timer?.Dispose();

    private Task AutoCheckAsync() => settings.Current.General.CheckForUpdates ? CheckAsync() : Task.CompletedTask;

    private void SetStatus(UpdateStatus status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }
}
