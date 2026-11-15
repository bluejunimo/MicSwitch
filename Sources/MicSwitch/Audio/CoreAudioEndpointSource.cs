using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using EndpointState = NAudio.CoreAudioApi.DeviceState;

namespace MicSwitch.Audio;

/// <summary>Windows Core Audio (WASAPI) devices. Use from the audio thread only.</summary>
internal sealed class CoreAudioEndpointSource : IEndpointSource, IDisposable
{
    private readonly NotificationClient notifications;
    private MMDeviceEnumerator? enumerator;

    public CoreAudioEndpointSource()
    {
        notifications = new NotificationClient(() => DevicesChanged?.Invoke());
    }

    public event Action? DevicesChanged;

    private MMDeviceEnumerator Enumerator
    {
        get
        {
            if (enumerator == null)
            {
                enumerator = new MMDeviceEnumerator();
                enumerator.RegisterEndpointNotificationCallback(notifications);
            }

            return enumerator;
        }
    }

    public IReadOnlyList<EndpointInfo> GetActiveEndpoints(AudioFlow flow)
    {
        var result = new List<EndpointInfo>();
        foreach (var device in Enumerator.EnumerateAudioEndPoints(ToDataFlow(flow), EndpointState.Active))
        {
            using (device)
            {
                result.Add(new EndpointInfo(device.ID, device.FriendlyName));
            }
        }

        return result;
    }

    public IReadOnlyList<string> GetDefaultEndpointIds(AudioFlow flow)
    {
        // Chat apps record from the "communications" default, everything else from the regular one.
        // For microphones both count; for speakers only the regular default does.
        Role[] roles = flow == AudioFlow.Capture ? [Role.Console, Role.Communications] : [Role.Console];
        var result = new List<string>();
        foreach (var role in roles)
        {
            if (!Enumerator.HasDefaultAudioEndpoint(ToDataFlow(flow), role))
            {
                continue;
            }

            using var device = Enumerator.GetDefaultAudioEndpoint(ToDataFlow(flow), role);
            if (!result.Contains(device.ID, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(device.ID);
            }
        }

        return result;
    }

    public IAudioEndpoint? Open(string id)
    {
        var device = OpenDevice(id);
        return device == null ? null : new CoreAudioEndpoint(device);
    }

    /// <summary>Returns the active device with this ID, or null. The caller disposes it.</summary>
    public MMDevice? OpenDevice(string id)
    {
        MMDevice? device = null;
        try
        {
            device = Enumerator.GetDevice(id);
            if (device.State == EndpointState.Active)
            {
                return device;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Device {Id} is not available", id);
        }

        device?.Dispose();
        return null;
    }

    public MMDevice? OpenDefaultDevice(AudioFlow flow)
    {
        var dataFlow = ToDataFlow(flow);
        return Enumerator.HasDefaultAudioEndpoint(dataFlow, Role.Console)
            ? Enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Console)
            : null;
    }

    public void Reset()
    {
        ReleaseEnumerator();
        _ = Enumerator;
    }

    public void Dispose() => ReleaseEnumerator();

    private void ReleaseEnumerator()
    {
        var old = enumerator;
        enumerator = null;
        if (old == null)
        {
            return;
        }

        try
        {
            old.UnregisterEndpointNotificationCallback(notifications);
            old.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error while releasing the device enumerator");
        }
    }

    private static DataFlow ToDataFlow(AudioFlow flow) => flow == AudioFlow.Capture ? DataFlow.Capture : DataFlow.Render;

    /// <summary>
    /// Called by Windows on its own threads. It must not call back into Core Audio from here,
    /// so it only signals; the listener rescans later on the audio thread.
    /// </summary>
    private sealed class NotificationClient(Action signal) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, EndpointState newState) => signal();

        public void OnDeviceAdded(string pwstrDeviceId) => signal();

        public void OnDeviceRemoved(string deviceId) => signal();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => signal();

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            // Fires constantly for unrelated properties. Renames are picked up by the periodic rescan.
        }
    }

    private sealed class CoreAudioEndpoint : IAudioEndpoint
    {
        private readonly MMDevice device;
        private readonly AudioEndpointVolume volume;

        public CoreAudioEndpoint(MMDevice device)
        {
            this.device = device;
            try
            {
                Info = new EndpointInfo(device.ID, device.FriendlyName);
                volume = device.AudioEndpointVolume;
                volume.OnVolumeNotification += OnVolumeNotification;
            }
            catch
            {
                device.Dispose();
                throw;
            }
        }

        public EndpointInfo Info { get; }

        public event Action? Changed;

        public bool Mute
        {
            get => volume.Mute;
            set
            {
                if (volume.Mute != value)
                {
                    volume.Mute = value;
                }
            }
        }

        public float Volume
        {
            get => volume.MasterVolumeLevelScalar;
            set => volume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f);
        }

        private void OnVolumeNotification(AudioVolumeNotificationData data) => Changed?.Invoke();

        public void Dispose()
        {
            volume.OnVolumeNotification -= OnVolumeNotification;

            // Also releases the endpoint volume. Throws if the device was already invalidated,
            // which the caller treats as "gone".
            device.Dispose();
        }
    }
}
