using CommunityToolkit.Mvvm.ComponentModel;
using MicSwitch.Settings;
using MicSwitch.Shell;

namespace MicSwitch.UI.ViewModels;

/// <summary>The mute mode and hotkeys of the microphone. <see cref="SpeakerHotkeysViewModel"/> is the same for the speakers.</summary>
internal partial class HotkeysViewModel : ObservableObject
{
    private readonly DeviceProfile profile;
    private readonly AppState state;
    private readonly SettingsStore settings;
    private bool syncing;

    [ObservableProperty]
    private MuteMode mode;

    [ObservableProperty]
    private bool extraHotkeysEnabled;

    public HotkeysViewModel(AppState state, SettingsStore settings)
        : this(DeviceProfile.Microphone, state, settings)
    {
    }

    protected HotkeysViewModel(DeviceProfile profile, AppState state, SettingsStore settings)
    {
        this.profile = profile;
        this.state = state;
        this.settings = settings;

        // "Talk" only makes sense for a microphone.
        var holdToUnmute = profile.IsMicrophone ? "Push-to-talk" : "Hold to unmute";
        var holdToMute = profile.IsMicrophone ? "Push-to-mute" : "Hold to mute";
        Modes =
        [
            new(MuteMode.Toggle, "Toggle"),
            new(MuteMode.PushToTalk, holdToUnmute),
            new(MuteMode.PushToMute, holdToMute)
        ];

        Hotkey = new HotkeyEditorViewModel("Hotkey", settings, s => profile.Device(s).Hotkey);
        var extra = new List<HotkeyEditorViewModel>
        {
            new("Toggle", settings, s => profile.Device(s).ToggleHotkey),
            new("Mute", settings, s => profile.Device(s).MuteHotkey),
            new("Unmute", settings, s => profile.Device(s).UnmuteHotkey),
            new(holdToUnmute, settings, s => profile.Device(s).PushToTalkHotkey),
            new(holdToMute, settings, s => profile.Device(s).PushToMuteHotkey)
        };
        if (!profile.IsMicrophone)
        {
            extra.Add(new HotkeyEditorViewModel("Volume up", settings, s => s.Speakers.VolumeUpHotkey));
            extra.Add(new HotkeyEditorViewModel("Volume down", settings, s => s.Speakers.VolumeDownHotkey));
        }

        ExtraHotkeys = extra;
        ExtraHotkeysDescription = profile.IsMicrophone
            ? "Additional hotkeys for other modes."
            : "Additional hotkeys for other modes and for the volume.";
        IsNotElevated = !ElevatedStart.IsElevated;

        state.SettingsChanged += Sync;
        Sync();
    }

    public IReadOnlyList<Option<MuteMode>> Modes { get; }

    public HotkeyEditorViewModel Hotkey { get; }

    public IReadOnlyList<HotkeyEditorViewModel> ExtraHotkeys { get; }

    public string ExtraHotkeysDescription { get; }

    /// <summary>Windows hides input meant for an administrator's window from ordinary apps.</summary>
    public bool IsNotElevated { get; }

    private void Sync()
    {
        var current = state.Settings;
        var device = profile.Device(current);
        syncing = true;
        try
        {
            Mode = device.Mode;
            ExtraHotkeysEnabled = device.ExtraHotkeysEnabled;
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
            settings.Update(s => profile.Device(s).Mode = value);
        }
    }

    partial void OnExtraHotkeysEnabledChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s => profile.Device(s).ExtraHotkeysEnabled = value);
        }
    }
}

internal sealed class SpeakerHotkeysViewModel(AppState state, SettingsStore settings)
    : HotkeysViewModel(DeviceProfile.Speakers, state, settings);
