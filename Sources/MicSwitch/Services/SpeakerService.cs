using MicSwitch.Audio;
using MicSwitch.Hotkeys;
using MicSwitch.Infrastructure;
using MicSwitch.Settings;

namespace MicSwitch.Services;

/// <summary>Speaker mute and volume hotkeys. Runs on the audio thread.</summary>
internal sealed class SpeakerService
{
    private const float FirstStep = 0.02f;
    private const float RepeatStep = 0.01f;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(25);

    private readonly DeviceController speakers;
    private readonly SettingsStore settings;
    private readonly IWorkQueue queue;
    private IDisposable? repeat;

    public SpeakerService(DeviceController speakers, SettingsStore settings, IWorkQueue queue)
    {
        this.speakers = speakers;
        this.settings = settings;
        this.queue = queue;
    }

    public void Start()
    {
        speakers.SelectionRebound += selection => settings.Update(s => s.Speakers.Device = selection);
        settings.Changed += changed => speakers.Select(changed.Speakers.Device);
        speakers.Select(settings.Current.Speakers.Device);
        speakers.Start();
    }

    /// <summary>Handles a hotkey press or release. Call on the audio thread.</summary>
    public void OnHotkey(HotkeyAction action, bool isDown)
    {
        switch (action)
        {
            case HotkeyAction.SpeakerToggle when isDown:
                speakers.ToggleMute();
                break;
            case HotkeyAction.SpeakerMute when isDown:
                speakers.SetMute(true);
                break;
            case HotkeyAction.SpeakerUnmute when isDown:
                speakers.SetMute(false);
                break;
            case HotkeyAction.SpeakerVolumeUp:
                HoldVolume(isDown, +1);
                break;
            case HotkeyAction.SpeakerVolumeDown:
                HoldVolume(isDown, -1);
                break;
        }
    }

    private void HoldVolume(bool isDown, int direction)
    {
        repeat?.Dispose();
        repeat = null;
        if (isDown)
        {
            speakers.AdjustVolume(direction * FirstStep);
            Repeat(direction, RepeatDelay);
        }
    }

    private void Repeat(int direction, TimeSpan delay)
    {
        repeat = queue.Schedule(delay, () =>
        {
            speakers.AdjustVolume(direction * RepeatStep);
            Repeat(direction, RepeatInterval);
        });
    }
}
