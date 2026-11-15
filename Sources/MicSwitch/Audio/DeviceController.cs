using MicSwitch.Infrastructure;

namespace MicSwitch.Audio;

/// <summary>
/// Keeps a <see cref="DeviceSelection"/> bound to real devices as they come and go, and exposes their
/// combined mute/volume. All work happens on the work queue; public methods can be called from any thread.
/// </summary>
internal sealed class DeviceController : IDisposable
{
    // Devices flap for a moment while a driver loads, so wait for things to settle.
    private static readonly TimeSpan ChangeDebounce = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(60);

    private readonly IEndpointSource source;
    private readonly IWorkQueue queue;
    private readonly AudioFlow flow;
    private readonly ILogger log;
    private readonly Dictionary<string, IAudioEndpoint> endpoints = new(StringComparer.OrdinalIgnoreCase);

    private DeviceSelection selection = DeviceSelection.Default;
    private IReadOnlyList<EndpointInfo> available = [];
    private DeviceState state = DeviceState.Empty;
    private IDisposable? pendingRefresh;
    private IDisposable? rescan;
    private bool settled;
    private bool? lastMute;
    private float? lastVolume;
    private int enumerationFailures;
    private int publishQueued;
    private bool disposed;

    public DeviceController(IEndpointSource source, IWorkQueue queue, AudioFlow flow)
    {
        this.source = source;
        this.queue = queue;
        this.flow = flow;
        log = Log.ForContext<DeviceController>();
        source.DevicesChanged += RequestRefresh;
    }

    public DeviceState State => Volatile.Read(ref state);

    /// <summary>
    /// With "all devices" selected, a device that shows up later is given the current mute state.
    /// When this is set it is given the current volume too.
    /// </summary>
    public bool PropagateVolume { get; set; }

    /// <summary>Raised on the work queue.</summary>
    public event Action<DeviceState>? StateChanged;

    /// <summary>
    /// The selected device was found again under a new ID or name. Raised on the work queue so the
    /// new identity can be saved.
    /// </summary>
    public event Action<DeviceSelection>? SelectionRebound;

    public void Start() => queue.Post(() =>
    {
        Refresh();
        ScheduleRescan();
    });

    public void Select(DeviceSelection newSelection) => queue.Post(() =>
    {
        if (disposed || newSelection == selection)
        {
            return;
        }

        log.Information("{Flow}: selecting {Selection}", flow, newSelection);
        selection = newSelection;
        settled = false;
        lastMute = null;
        lastVolume = null;
        ReleaseAll();
        Refresh();
    });

    public void SetMute(bool mute) => queue.Post(() => SetMuteCore(mute));

    public void ToggleMute() => queue.Post(() =>
    {
        if (ReadAggregate().Mute is { } current)
        {
            SetMuteCore(!current);
        }
    });

    public void SetVolume(float volume) => queue.Post(() => SetVolumeCore(volume));

    public void AdjustVolume(float delta) => queue.Post(() =>
    {
        if (ReadAggregate().Volume is { } current)
        {
            SetVolumeCore(current + delta);
        }
    });

    /// <summary>Asks for a rescan soon. Safe to call often and from any thread.</summary>
    public void RequestRefresh() => queue.Post(() => ScheduleRefresh(ChangeDebounce));

    public void Dispose()
    {
        source.DevicesChanged -= RequestRefresh;
        queue.Post(() =>
        {
            disposed = true;
            pendingRefresh?.Dispose();
            rescan?.Dispose();
            ReleaseAll();
        });
    }

    private void SetMuteCore(bool mute)
    {
        if (disposed)
        {
            return;
        }

        foreach (var endpoint in endpoints.Values.ToList())
        {
            Guarded(endpoint, x => x.Mute = mute);
        }

        Publish();
    }

    private void SetVolumeCore(float volume)
    {
        if (disposed)
        {
            return;
        }

        volume = Math.Clamp(volume, 0f, 1f);
        foreach (var endpoint in endpoints.Values.ToList())
        {
            Guarded(endpoint, x => x.Volume = volume);
        }

        Publish();
    }

    private void ScheduleRefresh(TimeSpan delay)
    {
        if (disposed)
        {
            return;
        }

        pendingRefresh?.Dispose();
        pendingRefresh = queue.Schedule(delay, Refresh);
    }

    private void ScheduleRescan()
    {
        if (disposed)
        {
            return;
        }

        rescan?.Dispose();
        rescan = queue.Schedule(RescanInterval, () =>
        {
            Refresh();
            ScheduleRescan();
        });
    }

    private void Refresh()
    {
        if (disposed)
        {
            return;
        }

        pendingRefresh?.Dispose();
        pendingRefresh = null;

        List<string> targets;
        try
        {
            available = source.GetActiveEndpoints(flow);
            targets = Resolve();
            enumerationFailures = 0;
        }
        catch (Exception ex)
        {
            // Typically the Windows Audio service restarting. Everything we hold is stale by now.
            enumerationFailures++;
            log.Warning(ex, "{Flow}: could not list devices (attempt {Attempt})", flow, enumerationFailures);
            available = [];
            ReleaseAll();
            if (enumerationFailures >= 2)
            {
                TryReset();
            }

            Publish();
            ScheduleRefresh(RetryDelay);
            return;
        }

        foreach (var id in endpoints.Keys.Where(id => !targets.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            Release(id);
        }

        var retry = false;
        foreach (var id in targets.Where(id => !endpoints.ContainsKey(id)))
        {
            try
            {
                var endpoint = source.Open(id);
                if (endpoint == null)
                {
                    retry = true;
                    continue;
                }

                endpoint.Changed += OnEndpointChanged;
                endpoints[id] = endpoint;
                log.Information("{Flow}: bound to {Device}", flow, endpoint.Info);

                if (settled && selection.Kind == DeviceSelectionKind.All)
                {
                    ApplyCurrentTo(endpoint);
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Could not open device {Id}", id);
                retry = true;
            }
        }

        settled = true;
        Publish();
        if (retry)
        {
            ScheduleRefresh(RetryDelay);
        }
    }

    private List<string> Resolve()
    {
        switch (selection.Kind)
        {
            case DeviceSelectionKind.All:
                return available.Select(x => x.Id).ToList();

            case DeviceSelectionKind.WindowsDefault:
                return source.GetDefaultEndpointIds(flow)
                    .Where(id => available.Any(x => SameId(x.Id, id)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            default:
                var match = available.FirstOrDefault(x => SameId(x.Id, selection.Id))
                            ?? DeviceNameMatcher.FindMatch(selection.Name, available);
                if (match == null)
                {
                    // Stay on the saved choice and wait for it; never switch to some other device.
                    return [];
                }

                if (!SameId(match.Id, selection.Id) || match.Name != selection.Name)
                {
                    log.Information("{Flow}: {Old} is now {New}", flow, selection, match);
                    selection = DeviceSelection.Specific(match.Id, match.Name);
                    SelectionRebound?.Invoke(selection);
                }

                return [match.Id];
        }
    }

    private void ApplyCurrentTo(IAudioEndpoint endpoint)
    {
        if (lastMute is { } mute)
        {
            Guarded(endpoint, x => x.Mute = mute);
        }

        if (PropagateVolume && lastVolume is { } volume)
        {
            Guarded(endpoint, x => x.Volume = volume);
        }
    }

    private void OnEndpointChanged()
    {
        // Volume callbacks arrive in bursts (e.g. while a slider is dragged); one publish covers them.
        if (Interlocked.Exchange(ref publishQueued, 1) == 0)
        {
            queue.Post(() =>
            {
                Interlocked.Exchange(ref publishQueued, 0);
                Publish();
            });
        }
    }

    private (bool? Mute, float? Volume) ReadAggregate()
    {
        var allMuted = true;
        var volumeSum = 0f;
        var count = 0;
        foreach (var endpoint in endpoints.Values.ToList())
        {
            if (!Guarded(endpoint, x =>
                {
                    allMuted &= x.Mute;
                    volumeSum += x.Volume;
                }))
            {
                continue;
            }

            count++;
        }

        return count == 0 ? (null, null) : (allMuted, volumeSum / count);
    }

    private void Publish()
    {
        if (disposed)
        {
            return;
        }

        var (mute, volume) = ReadAggregate();
        lastMute = mute ?? lastMute;
        lastVolume = volume ?? lastVolume;

        var bound = endpoints.Values.Select(x => x.Info).ToList();
        var next = new DeviceState(selection, available, bound, mute, volume);
        if (next.Equals(state))
        {
            return;
        }

        Volatile.Write(ref state, next);
        StateChanged?.Invoke(next);
    }

    /// <summary>Runs <paramref name="action"/> against a device; a failure means the device is gone.</summary>
    private bool Guarded(IAudioEndpoint endpoint, Action<IAudioEndpoint> action)
    {
        try
        {
            action(endpoint);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "{Flow}: lost device {Device}", flow, endpoint.Info);
            Release(endpoint.Info.Id);
            ScheduleRefresh(ChangeDebounce);
            return false;
        }
    }

    private void Release(string id)
    {
        if (!endpoints.Remove(id, out var endpoint))
        {
            return;
        }

        endpoint.Changed -= OnEndpointChanged;
        try
        {
            endpoint.Dispose();
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Error while releasing {Device}", endpoint.Info);
        }

        log.Information("{Flow}: released {Device}", flow, endpoint.Info);
    }

    private void ReleaseAll()
    {
        foreach (var id in endpoints.Keys.ToList())
        {
            Release(id);
        }
    }

    private void TryReset()
    {
        try
        {
            log.Information("Reconnecting to the audio service");
            source.Reset();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not reconnect to the audio service");
        }
    }

    private static bool SameId(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
