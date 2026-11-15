using MicSwitch.Audio;
using MicSwitch.Hotkeys;
using MicSwitch.Infrastructure;

namespace MicSwitch.Services;

/// <summary>The speaker volume hotkeys, which repeat while held. Runs on the audio thread.</summary>
internal sealed class SpeakerService
{
    private const float FirstStep = 0.02f;
    private const float RepeatStep = 0.01f;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(25);

    private readonly DeviceController speakers;
    private readonly IWorkQueue queue;
    private IDisposable? repeat;

    public SpeakerService(DeviceController speakers, IWorkQueue queue)
    {
        this.speakers = speakers;
        this.queue = queue;
    }

    /// <summary>Handles a hotkey press or release. Call on the audio thread.</summary>
    public void OnHotkey(HotkeyAction action, bool isDown)
    {
        switch (action)
        {
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
