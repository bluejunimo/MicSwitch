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
    private readonly MicrophoneService microphone;
    private readonly SpeakerService speakers;
    private readonly HotkeyMatcher matcher = new();
    private readonly InputHook hook;
    private readonly Timer reinstallTimer;
    private readonly object gate = new();
    private HotkeyBinding[] applied = [];

    public HotkeyService(SettingsStore settings, IWorkQueue queue, MicrophoneService microphone, SpeakerService speakers)
    {
        this.settings = settings;
        this.queue = queue;
        this.microphone = microphone;
        this.speakers = speakers;
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
        var mic = current.Microphone;
        var result = new List<HotkeyBinding>();
        Add(HotkeyAction.MicMain, mic.Hotkey);
        if (mic.ExtraHotkeysEnabled)
        {
            Add(HotkeyAction.MicToggle, mic.ToggleHotkey);
            Add(HotkeyAction.MicMute, mic.MuteHotkey);
            Add(HotkeyAction.MicUnmute, mic.UnmuteHotkey);
            Add(HotkeyAction.MicPushToTalk, mic.PushToTalkHotkey);
            Add(HotkeyAction.MicPushToMute, mic.PushToMuteHotkey);
        }

        var speaker = current.Speakers;
        if (speaker.Enabled)
        {
            Add(HotkeyAction.SpeakerToggle, speaker.ToggleHotkey);
            Add(HotkeyAction.SpeakerMute, speaker.MuteHotkey);
            Add(HotkeyAction.SpeakerUnmute, speaker.UnmuteHotkey);
            Add(HotkeyAction.SpeakerVolumeUp, speaker.VolumeUpHotkey);
            Add(HotkeyAction.SpeakerVolumeDown, speaker.VolumeDownHotkey);
        }

        return result;

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
        if (binding.Action >= HotkeyAction.SpeakerToggle)
        {
            speakers.OnHotkey(binding.Action, isDown);
        }
        else
        {
            microphone.OnHotkey(binding.Action, isDown);
        }
    });
}
