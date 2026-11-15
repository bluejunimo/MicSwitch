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

internal enum AppTheme
{
    System,
    Light,
    Dark
}

internal sealed class HotkeySetting
{
    public string Key { get; set; } = "";
    public string AltKey { get; set; } = "";
    public bool Suppress { get; set; } = true;
    public bool IgnoreModifiers { get; set; }
}

/// <summary>What the microphone and the speakers have in common: which device, and how it is muted.</summary>
internal class DeviceSettings
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

internal sealed class MicrophoneSettings : DeviceSettings;

internal sealed class SoundSettings
{
    public const string DefaultMuteSound = "Discord Mute";
    public const string DefaultUnmuteSound = "Discord Unmute";

    /// <summary>No sounds until the user picks some.</summary>
    public static SoundSettings Silent => new() { MuteSound = string.Empty, UnmuteSound = string.Empty };

    public string MuteSound { get; set; } = DefaultMuteSound;
    public string UnmuteSound { get; set; } = DefaultUnmuteSound;
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

internal sealed class SpeakerSettings : DeviceSettings
{
    /// <summary>Settings version 1 switched all speaker hotkeys with this. Now <see cref="DeviceSettings.ExtraHotkeysEnabled"/>.</summary>
    public bool? Enabled { get; set; }

    // Used together with the extra hotkeys.
    public HotkeySetting VolumeUpHotkey { get; set; } = new();
    public HotkeySetting VolumeDownHotkey { get; set; } = new();

    public SoundSettings Sounds { get; set; } = SoundSettings.Silent;
    public OverlaySettings Overlay { get; set; } = new();
}

/// <summary>Where the settings of the microphone or of the speakers are found.</summary>
internal sealed class DeviceProfile
{
    public static readonly DeviceProfile Microphone = new(AudioFlow.Capture, s => s.Microphone, s => s.Sounds, s => s.Overlay);
    public static readonly DeviceProfile Speakers = new(AudioFlow.Render, s => s.Speakers, s => s.Speakers.Sounds, s => s.Speakers.Overlay);

    private DeviceProfile(
        AudioFlow flow,
        Func<AppSettings, DeviceSettings> device,
        Func<AppSettings, SoundSettings> sounds,
        Func<AppSettings, OverlaySettings> overlay)
    {
        Flow = flow;
        Device = device;
        Sounds = sounds;
        Overlay = overlay;
    }

    public AudioFlow Flow { get; }

    public bool IsMicrophone => Flow == AudioFlow.Capture;

    public Func<AppSettings, DeviceSettings> Device { get; }

    public Func<AppSettings, SoundSettings> Sounds { get; }

    public Func<AppSettings, OverlaySettings> Overlay { get; }
}

internal sealed class GeneralSettings
{
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>The executable that the "run as administrator" task was made for; null when that is off.</summary>
    public string? ElevatedExecutable { get; set; }

    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
}

internal sealed class AppSettings
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public MicrophoneSettings Microphone { get; set; } = new();

    // The microphone's sounds and overlay; the speakers keep theirs inside Speakers.
    public SoundSettings Sounds { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public SpeakerSettings Speakers { get; set; } = new();
    public GeneralSettings General { get; set; } = new();

    /// <summary>Brings settings saved by an earlier version up to date.</summary>
    public void Upgrade()
    {
        if (Version < 2 && Speakers.Enabled is { } enabled)
        {
            Speakers.ExtraHotkeysEnabled = enabled;
        }

        Speakers.Enabled = null;
        Version = CurrentVersion;
    }
}
