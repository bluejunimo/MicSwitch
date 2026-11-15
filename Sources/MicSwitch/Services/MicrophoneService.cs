using MicSwitch.Audio;
using MicSwitch.Hotkeys;
using MicSwitch.Infrastructure;
using MicSwitch.Settings;

namespace MicSwitch.Services;

/// <summary>
/// Applies the microphone settings: which device, what the hotkeys do in each mute mode, the state
/// to start in, and the sound played when the mute state changes. Runs on the audio thread.
/// </summary>
internal sealed class MicrophoneService
{
    private readonly DeviceController microphone;
    private readonly NotificationPlayer player;
    private readonly SettingsStore settings;
    private readonly IWorkQueue queue;

    private DeviceSelection? appliedDevice;
    private MuteMode? appliedMode;

    // State to put the microphone in as soon as it is connected.
    private bool? pendingMute;
    private bool pendingMuteIsSilent;
    private float? pendingVolume;

    // The mute state the user last heard a sound for (or started from).
    private bool? heardMute;

    public MicrophoneService(DeviceController microphone, NotificationPlayer player, SettingsStore settings, IWorkQueue queue)
    {
        this.microphone = microphone;
        this.player = player;
        this.settings = settings;
        this.queue = queue;
    }

    public void Start()
    {
        microphone.StateChanged += OnStateChanged;
        microphone.SelectionRebound += selection => settings.Update(s => s.Microphone.Device = selection);
        settings.Changed += changed => queue.Post(() => Apply(changed));
        queue.Post(() =>
        {
            var current = settings.Current;
            if (current.Microphone.RestoreVolume)
            {
                pendingVolume = current.Microphone.Volume;
            }

            Apply(current);
            microphone.Start();
        });
    }

    /// <summary>Handles a hotkey press or release. Call on the audio thread.</summary>
    public void OnHotkey(HotkeyAction action, bool isDown)
    {
        switch (action)
        {
            case HotkeyAction.MicMain:
                switch (settings.Current.Microphone.Mode)
                {
                    case MuteMode.PushToTalk:
                        microphone.SetMute(!isDown);
                        break;
                    case MuteMode.PushToMute:
                        microphone.SetMute(isDown);
                        break;
                    default:
                        if (isDown)
                        {
                            microphone.ToggleMute();
                        }

                        break;
                }

                break;

            case HotkeyAction.MicToggle when isDown:
                microphone.ToggleMute();
                break;
            case HotkeyAction.MicMute when isDown:
                microphone.SetMute(true);
                break;
            case HotkeyAction.MicUnmute when isDown:
                microphone.SetMute(false);
                break;
            case HotkeyAction.MicPushToTalk:
                microphone.SetMute(!isDown);
                break;
            case HotkeyAction.MicPushToMute:
                microphone.SetMute(isDown);
                break;
        }
    }

    private void Apply(AppSettings current)
    {
        var mic = current.Microphone;
        microphone.PropagateVolume = mic.RestoreVolume;
        player.SetDevice(current.Sounds.OutputDevice);

        if (appliedDevice != mic.Device)
        {
            appliedDevice = mic.Device;
            heardMute = null;
            microphone.Select(mic.Device);
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
                if (microphone.State is { IsConnected: true, Mute: not null } state)
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
            // No sound when the microphone comes back; its state then is simply the new starting point.
            heardMute = null;
            return;
        }

        var current = settings.Current;
        var baseline = mute;

        if (pendingVolume is { } volume)
        {
            pendingVolume = null;
            microphone.SetVolume(volume);
        }
        else if (current.Microphone.RestoreVolume && state.Volume is { } actual &&
                 Math.Abs(actual - (current.Microphone.Volume ?? -1f)) > 0.004f)
        {
            settings.Update(s => s.Microphone.Volume = actual);
        }

        if (pendingMute is { } wanted)
        {
            pendingMute = null;
            if (wanted != mute)
            {
                microphone.SetMute(wanted);
                if (pendingMuteIsSilent)
                {
                    baseline = wanted;
                }
            }
        }

        if (heardMute is { } previous && previous != mute)
        {
            player.Play(mute ? current.Sounds.MuteSound : current.Sounds.UnmuteSound, current.Sounds.Volume);
        }

        heardMute = baseline;
    }
}
