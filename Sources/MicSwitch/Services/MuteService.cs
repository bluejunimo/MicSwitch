using MicSwitch.Audio;
using MicSwitch.Infrastructure;
using MicSwitch.Settings;

namespace MicSwitch.Services;

/// <summary>What a mute hotkey does.</summary>
internal enum MuteAction
{
    /// <summary>The main hotkey: what it does depends on the mute mode.</summary>
    Main,
    Toggle,
    Mute,
    Unmute,
    PushToTalk,
    PushToMute
}

/// <summary>
/// Applies the settings of the microphone or of the speakers: which device, what the hotkeys do in each
/// mute mode, the state to start in, and the sound played when the mute state changes. Runs on the audio thread.
/// </summary>
internal sealed class MuteService
{
    private readonly DeviceProfile profile;
    private readonly DeviceController device;
    private readonly NotificationPlayer player;
    private readonly SettingsStore settings;
    private readonly IWorkQueue queue;

    private DeviceSelection? appliedDevice;
    private MuteMode? appliedMode;

    // State to put the device in as soon as it is connected.
    private bool? pendingMute;
    private bool pendingMuteIsSilent;
    private float? pendingVolume;

    // The mute state the user last heard a sound for (or started from).
    private bool? heardMute;

    public MuteService(DeviceProfile profile, DeviceController device, NotificationPlayer player, SettingsStore settings, IWorkQueue queue)
    {
        this.profile = profile;
        this.device = device;
        this.player = player;
        this.settings = settings;
        this.queue = queue;
    }

    public void Start()
    {
        device.StateChanged += OnStateChanged;
        device.SelectionRebound += selection => settings.Update(s => profile.Device(s).Device = selection);
        settings.Changed += changed => queue.Post(() => Apply(changed));
        queue.Post(() =>
        {
            var current = settings.Current;
            if (profile.Device(current) is { RestoreVolume: true } saved)
            {
                pendingVolume = saved.Volume;
            }

            Apply(current);
            device.Start();
        });
    }

    /// <summary>Handles a hotkey press or release. Call on the audio thread.</summary>
    public void OnHotkey(MuteAction action, bool isDown)
    {
        switch (action)
        {
            case MuteAction.Main:
                switch (profile.Device(settings.Current).Mode)
                {
                    case MuteMode.PushToTalk:
                        device.SetMute(!isDown);
                        break;
                    case MuteMode.PushToMute:
                        device.SetMute(isDown);
                        break;
                    default:
                        if (isDown)
                        {
                            device.ToggleMute();
                        }

                        break;
                }

                break;

            case MuteAction.Toggle when isDown:
                device.ToggleMute();
                break;
            case MuteAction.Mute when isDown:
                device.SetMute(true);
                break;
            case MuteAction.Unmute when isDown:
                device.SetMute(false);
                break;
            case MuteAction.PushToTalk:
                device.SetMute(!isDown);
                break;
            case MuteAction.PushToMute:
                device.SetMute(isDown);
                break;
        }
    }

    private void Apply(AppSettings current)
    {
        var mic = profile.Device(current);
        device.PropagateVolume = mic.RestoreVolume;
        player.SetDevice(profile.Sounds(current).OutputDevice);

        if (appliedDevice != mic.Device)
        {
            appliedDevice = mic.Device;
            heardMute = null;
            device.Select(mic.Device);
        }

        if (appliedMode != mic.Mode)
        {
            var isStartup = appliedMode == null;
            appliedMode = mic.Mode;
            bool? wanted = mic.Mode switch
            {
                MuteMode.PushToTalk => true,
                MuteMode.PushToMute => false,
                _ when isStartup => mic.StartupState switch
                {
                    StartupMicState.Mute => true,
                    StartupMicState.Unmute => false,
                    _ => null
                },
                _ => null
            };

            if (wanted != null)
            {
                pendingMute = wanted;
                pendingMuteIsSilent = isStartup;
                if (device.State is { IsConnected: true, Mute: not null } state)
                {
                    OnStateChanged(state);
                }
            }
        }
    }

    private void OnStateChanged(DeviceState state)
    {
        if (!state.IsConnected || state.Mute is not { } mute)
        {
            // No sound when the device comes back; its state then is simply the new starting point.
            heardMute = null;
            return;
        }

        var current = settings.Current;
        var saved = profile.Device(current);
        var sounds = profile.Sounds(current);
        var baseline = mute;

        if (pendingVolume is { } volume)
        {
            pendingVolume = null;
            device.SetVolume(volume);
        }
        else if (saved.RestoreVolume && state.Volume is { } actual && Math.Abs(actual - (saved.Volume ?? -1f)) > 0.004f)
        {
            settings.Update(s => profile.Device(s).Volume = actual);
        }

        if (pendingMute is { } wanted)
        {
            pendingMute = null;
            if (wanted != mute)
            {
                device.SetMute(wanted);
                if (pendingMuteIsSilent)
                {
                    baseline = wanted;
                }
            }
        }

        if (heardMute is { } previous && previous != mute)
        {
            var sound = mute ? sounds.MuteSound : sounds.UnmuteSound;
            if (!string.IsNullOrEmpty(sound))
            {
                player.Play(sound, sounds.Volume);
            }
        }

        heardMute = baseline;
    }
}
