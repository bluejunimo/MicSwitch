using MicSwitch.Hotkeys;
using MicSwitch.Infrastructure;
using MicSwitch.Settings;

namespace MicSwitch.Services;

/// <summary>Keeps the input hook in line with the hotkeys in settings and routes presses to the services.</summary>
internal sealed class HotkeyService : IDisposable
{
    private static readonly TimeSpan ReinstallInterval = TimeSpan.FromMinutes(10);

    private readonly SettingsStore settings;
    private readonly IWorkQueue queue;
    private readonly MuteService microphone;
    private readonly MuteService speakers;
    private readonly SpeakerService speakerVolume;
    private readonly HotkeyMatcher matcher = new();
    private readonly InputHook hook;
    private readonly Timer reinstallTimer;
    private readonly object gate = new();
    private HotkeyBinding[] applied = [];

    public HotkeyService(SettingsStore settings, IWorkQueue queue, MuteService microphone, MuteService speakers, SpeakerService speakerVolume)
    {
        this.settings = settings;
        this.queue = queue;
        this.microphone = microphone;
        this.speakers = speakers;
        this.speakerVolume = speakerVolume;
        hook = new InputHook(matcher, OnHotkey);
        reinstallTimer = new Timer(_ => hook.Reinstall(), null, ReinstallInterval, ReinstallInterval);
    }

    public void Start()
    {
        settings.Changed += Apply;
        Apply(settings.Current);
    }

    /// <summary>Hotkeys are ignored while the user is recording a new one.</summary>
    public bool Paused
    {
        get => hook.Paused;
        set => hook.Paused = value;
    }

    public void Reinstall() => hook.Reinstall();

    public void Dispose()
    {
        settings.Changed -= Apply;
        reinstallTimer.Dispose();
        hook.Dispose();
    }

    internal static IEnumerable<HotkeyBinding> BuildBindings(AppSettings current)
    {
        var result = new List<HotkeyBinding>();
        AddDevice(current.Microphone, HotkeyAction.MicMain);
        AddDevice(current.Speakers, HotkeyAction.SpeakerMain);
        if (current.Speakers.ExtraHotkeysEnabled)
        {
            Add(HotkeyAction.SpeakerVolumeUp, current.Speakers.VolumeUpHotkey);
            Add(HotkeyAction.SpeakerVolumeDown, current.Speakers.VolumeDownHotkey);
        }

        return result;

        // The six mute actions of a device follow each other in HotkeyAction, in MuteAction's order.
        void AddDevice(DeviceSettings device, HotkeyAction main)
        {
            Add(main + (int)MuteAction.Main, device.Hotkey);
            if (device.ExtraHotkeysEnabled)
            {
                Add(main + (int)MuteAction.Toggle, device.ToggleHotkey);
                Add(main + (int)MuteAction.Mute, device.MuteHotkey);
                Add(main + (int)MuteAction.Unmute, device.UnmuteHotkey);
                Add(main + (int)MuteAction.PushToTalk, device.PushToTalkHotkey);
                Add(main + (int)MuteAction.PushToMute, device.PushToMuteHotkey);
            }
        }

        void Add(HotkeyAction action, HotkeySetting hotkey)
        {
            foreach (var text in new[] { hotkey.Key, hotkey.AltKey })
            {
                var gesture = HotkeyGesture.Parse(text);
                if (!gesture.IsEmpty)
                {
                    result.Add(new HotkeyBinding(action, gesture, hotkey.Suppress, hotkey.IgnoreModifiers && !gesture.HasModifiers));
                }
            }
        }
    }

    private void Apply(AppSettings current)
    {
        lock (gate)
        {
            var bindings = BuildBindings(current).ToArray();
            if (bindings.SequenceEqual(applied))
            {
                return;
            }

            applied = bindings;
            matcher.SetBindings(bindings);
            hook.Configure(matcher.HasKeyboardBindings, matcher.HasMouseBindings);
            Log.Information("Hotkeys: {Hotkeys}", string.Join(", ", bindings.Select(x => $"{x.Action}={x.Gesture}")));
        }
    }

    // Called on the hook thread: hand over and return at once.
    private void OnHotkey(HotkeyBinding binding, bool isDown) => queue.Post(() =>
    {
        var action = binding.Action;
        if (action >= HotkeyAction.SpeakerVolumeUp)
        {
            speakerVolume.OnHotkey(action, isDown);
        }
        else if (action >= HotkeyAction.SpeakerMain)
        {
            speakers.OnHotkey((MuteAction)(action - HotkeyAction.SpeakerMain), isDown);
        }
        else
        {
            microphone.OnHotkey((MuteAction)(action - HotkeyAction.MicMain), isDown);
        }
    });
}
