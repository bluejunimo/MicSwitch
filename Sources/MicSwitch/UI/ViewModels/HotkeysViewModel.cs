using CommunityToolkit.Mvvm.ComponentModel;
using MicSwitch.Settings;
using MicSwitch.Shell;

namespace MicSwitch.UI.ViewModels;

internal sealed partial class HotkeysViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly SettingsStore settings;
    private bool syncing;

    [ObservableProperty]
    private MuteMode mode;

    [ObservableProperty]
    private bool extraHotkeysEnabled;

    public HotkeysViewModel(AppState state, SettingsStore settings)
    {
        this.state = state;
        this.settings = settings;
        Hotkey = new HotkeyEditorViewModel("Hotkey", settings, s => s.Microphone.Hotkey);
        ExtraHotkeys =
        [
            new HotkeyEditorViewModel("Toggle", settings, s => s.Microphone.ToggleHotkey),
            new HotkeyEditorViewModel("Mute", settings, s => s.Microphone.MuteHotkey),
            new HotkeyEditorViewModel("Unmute", settings, s => s.Microphone.UnmuteHotkey),
            new HotkeyEditorViewModel("Push-to-talk", settings, s => s.Microphone.PushToTalkHotkey),
            new HotkeyEditorViewModel("Push-to-mute", settings, s => s.Microphone.PushToMuteHotkey)
        ];

        IsNotElevated = !ElevatedStart.IsElevated;

        state.SettingsChanged += Sync;
        Sync();
    }

    public IReadOnlyList<Option<MuteMode>> Modes { get; } =
    [
        new(MuteMode.Toggle, "Toggle"),
        new(MuteMode.PushToTalk, "Push-to-talk"),
        new(MuteMode.PushToMute, "Push-to-mute")
    ];

    public HotkeyEditorViewModel Hotkey { get; }

    public IReadOnlyList<HotkeyEditorViewModel> ExtraHotkeys { get; }

    /// <summary>Windows hides input meant for an administrator's window from ordinary apps.</summary>
    public bool IsNotElevated { get; }

    private void Sync()
    {
        var current = state.Settings;
        syncing = true;
        try
        {
            Mode = current.Microphone.Mode;
            ExtraHotkeysEnabled = current.Microphone.ExtraHotkeysEnabled;
        }
        finally
        {
            syncing = false;
        }

        Hotkey.Load(current);
        foreach (var hotkey in ExtraHotkeys)
        {
            hotkey.Load(current);
        }
    }

    partial void OnModeChanged(MuteMode value)
    {
        if (!syncing)
        {
            settings.Update(s => s.Microphone.Mode = value);
        }
    }

    partial void OnExtraHotkeysEnabledChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s => s.Microphone.ExtraHotkeysEnabled = value);
        }
    }
}
