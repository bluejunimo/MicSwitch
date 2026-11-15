using MicSwitch.Audio;

namespace MicSwitch.Settings;

internal enum MuteMode
{
    Toggle,
    PushToTalk,
    PushToMute
}

internal enum StartupMicState
{
    Keep,
    Mute,
    Unmute
}

internal enum OverlayVisibility
{
    Always,
    WhenMuted,
    WhenUnmuted,
    Never
}

internal sealed class HotkeySetting
{
    public string Key { get; set; } = "";
    public string AltKey { get; set; } = "";
    public bool Suppress { get; set; } = true;
    public bool IgnoreModifiers { get; set; }
}

internal sealed class MicrophoneSettings
{
    public DeviceSelection Device { get; set; } = DeviceSelection.Default;
    public bool RestoreVolume { get; set; }
    public float? Volume { get; set; }
    public MuteMode Mode { get; set; } = MuteMode.Toggle;
    public StartupMicState StartupState { get; set; } = StartupMicState.Keep;
    public HotkeySetting Hotkey { get; set; } = new();
    public bool ExtraHotkeysEnabled { get; set; }
    public HotkeySetting ToggleHotkey { get; set; } = new();
    public HotkeySetting MuteHotkey { get; set; } = new();
    public HotkeySetting UnmuteHotkey { get; set; } = new();
    public HotkeySetting PushToTalkHotkey { get; set; } = new();
    public HotkeySetting PushToMuteHotkey { get; set; } = new();
}

internal sealed class SoundSettings
{
    public string MuteSound { get; set; } = "Beep750";
    public string UnmuteSound { get; set; } = "Beep300";
    public float Volume { get; set; } = 1;
    public DeviceSelection OutputDevice { get; set; } = DeviceSelection.Default;
}

internal sealed class OverlaySettings
{
    public OverlayVisibility Visibility { get; set; } = OverlayVisibility.Never;
    public double Opacity { get; set; } = 1;
    public bool Locked { get; set; } = true;

    // Screen pixels. Null until the overlay has been placed once.
    public int? X { get; set; }
    public int? Y { get; set; }
    public int Size { get; set; } = 96;

    public string? MicIconPath { get; set; }
    public string? MutedIconPath { get; set; }
}

internal sealed class SpeakerSettings
{
    public bool Enabled { get; set; }
    public DeviceSelection Device { get; set; } = DeviceSelection.Default;
    public HotkeySetting ToggleHotkey { get; set; } = new();
    public HotkeySetting MuteHotkey { get; set; } = new();
    public HotkeySetting UnmuteHotkey { get; set; } = new();
    public HotkeySetting VolumeUpHotkey { get; set; } = new();
    public HotkeySetting VolumeDownHotkey { get; set; } = new();
}

internal sealed class GeneralSettings
{
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
}

internal sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public MicrophoneSettings Microphone { get; set; } = new();
    public SoundSettings Sounds { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public SpeakerSettings Speakers { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
}
