using MicSwitch.Audio;
using MicSwitch.Settings;

namespace MicSwitch.Tests;

public class LegacySettingsImporterTests : IDisposable
{
    // A MicSwitch 1.0.253 config.cfg, shortened.
    private const string LegacyConfig = """
        {
          "AssemblyName": "PoeShared",
          "TypeName": "PoeShared.Modularity.ConfigProviderFromFile+PoeEyeCombinedConfig",
          "Version": 1,
          "ConfigValue": {
            "Version": 1,
            "Items": [
              {
                "TypeName": "PoeShared.Squirrel.Updater.UpdateSettingsConfig",
                "ConfigValue": { "AutoUpdateTimeout": "00:30:00", "UpdateSourceId": "Github", "Version": 11 }
              },
              {
                "TypeName": "MicSwitch.Modularity.MicSwitchConfig",
                "ConfigValue": {
                  "StartMinimized": true,
                  "MicrophoneLineId": { "LineId": "{0.0.1.00000000}.{d2ab49ba}", "Name": "Microphone (4- HyperX SoloCast)" },
                  "OutputDeviceId": "{ Name = Speakers (2- Xonar U7 MKII), ProductGuid = 00000000-0000-0000-0000-000000000000, ManufacturerGuid = 00000000-0000-0000-0000-000000000000, NameGuid = 00000000-0000-0000-0000-000000000000 }",
                  "MinimizeOnClose": true,
                  "VolumeControlEnabled": false,
                  "MainWindowBounds": "4142,711,600,430",
                  "Notifications": { "On": "unmute", "Off": "mute" },
                  "NotificationVolume": 0.5067551,
                  "Version": 1
                }
              },
              {
                "TypeName": "MicSwitch.Modularity.MicSwitchVolumeControlConfig",
                "ConfigValue": {
                  "IsEnabled": true,
                  "DeviceId": { "LineId": "all", "Name": "All devices" },
                  "HotkeyForVolumeUp": { "Key": "Ctrl+Up", "AlternativeKey": "None", "IgnoreModifiers": false, "Suppress": false },
                  "Version": 0
                }
              },
              {
                "TypeName": "MicSwitch.Modularity.MicSwitchOverlayConfig",
                "ConfigValue": {
                  "OverlayBounds": "362, 234, 120, 120",
                  "OverlayOpacity": 0.7,
                  "OverlayVisibilityMode": "WhenMuted",
                  "MicrophoneIcon": "iVBORw0KGgo=",
                  "Version": 1
                }
              },
              {
                "TypeName": "MicSwitch.Modularity.MicSwitchHotkeyConfig",
                "ConfigValue": {
                  "Hotkey": { "Key": "MouseXButton2", "AlternativeKey": "None", "IgnoreModifiers": true, "Suppress": true },
                  "MuteMode": "PushToMute",
                  "EnableAdvancedHotkeys": false,
                  "InitialMicrophoneState": "Mute",
                  "Version": 1
                }
              }
            ]
          }
        }
        """;

    private readonly string directory = Path.Combine(Path.GetTempPath(), "MicSwitch.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Carries_over_a_1x_setup()
    {
        var settings = LegacySettingsImporter.Import(LegacyConfig, Path.Combine(directory, "Icons"));

        Assert.Equal(DeviceSelection.Specific("{0.0.1.00000000}.{d2ab49ba}", "Microphone (4- HyperX SoloCast)"), settings.Microphone.Device);
        Assert.Equal(MuteMode.PushToMute, settings.Microphone.Mode);
        Assert.Equal(StartupMicState.Mute, settings.Microphone.StartupState);
        Assert.Equal("MouseXButton2", settings.Microphone.Hotkey.Key);
        Assert.Equal(string.Empty, settings.Microphone.Hotkey.AltKey);
        Assert.True(settings.Microphone.Hotkey.Suppress);
        Assert.True(settings.Microphone.Hotkey.IgnoreModifiers);
        Assert.False(settings.Microphone.ExtraHotkeysEnabled);
        Assert.False(settings.Microphone.RestoreVolume);

        Assert.Equal("mute", settings.Sounds.MuteSound);
        Assert.Equal("unmute", settings.Sounds.UnmuteSound);
        Assert.Equal(0.5067551f, settings.Sounds.Volume, 4);
        Assert.Equal(new DeviceSelection(DeviceSelectionKind.Specific, null, "Speakers (2- Xonar U7 MKII)"), settings.Sounds.OutputDevice);

        Assert.Equal(OverlayVisibility.WhenMuted, settings.Overlay.Visibility);
        Assert.Equal(0.7, settings.Overlay.Opacity, 3);
        Assert.Equal((362, 234, 120), (settings.Overlay.X, settings.Overlay.Y, settings.Overlay.Size));
        Assert.True(File.Exists(settings.Overlay.MicIconPath));
        Assert.EndsWith(".png", settings.Overlay.MicIconPath);
        Assert.Null(settings.Overlay.MutedIconPath);

        Assert.True(settings.Speakers.ExtraHotkeysEnabled);
        Assert.Equal(DeviceSelection.All, settings.Speakers.Device);
        Assert.Equal("Ctrl+Up", settings.Speakers.VolumeUpHotkey.Key);
        Assert.False(settings.Speakers.VolumeUpHotkey.Suppress);

        Assert.False(settings.General.StartMinimized);
        Assert.True(settings.General.CloseToTray);
    }

    [Fact]
    public void Copies_custom_sounds_and_survives_a_broken_file()
    {
        var legacySounds = Path.Combine(directory, "legacy");
        Directory.CreateDirectory(legacySounds);
        File.WriteAllText(Path.Combine(legacySounds, "mute.wav"), "x");
        var config = Path.Combine(directory, "config.cfg");
        File.WriteAllText(config, LegacyConfig);
        var sounds = Path.Combine(directory, "Sounds");

        Assert.NotNull(LegacySettingsImporter.TryImport(config, legacySounds, sounds, Path.Combine(directory, "Icons")));
        Assert.True(File.Exists(Path.Combine(sounds, "mute.wav")));

        File.WriteAllText(config, "{ not json");
        Assert.Null(LegacySettingsImporter.TryImport(config, legacySounds, sounds, Path.Combine(directory, "Icons")));
        Assert.Null(LegacySettingsImporter.TryImport(Path.Combine(directory, "missing.cfg"), legacySounds, sounds, directory));
    }
}

public class SettingsStoreTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "MicSwitch.Tests", Guid.NewGuid().ToString("N"), "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(path)))
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void Saves_and_reads_back_everything()
    {
        using (var store = new SettingsStore(path, new AppSettings()))
        {
            store.Update(s =>
            {
                s.Microphone.Device = DeviceSelection.Specific("id", "Mic");
                s.Microphone.Mode = MuteMode.PushToTalk;
                s.Microphone.Hotkey.Key = "MouseXButton2";
                s.Overlay.X = -1200;
                s.Sounds.Volume = 0.25f;
            });
        }

        var saved = SettingsStore.TryRead(path);

        Assert.NotNull(saved);
        Assert.Equal(DeviceSelection.Specific("id", "Mic"), saved.Microphone.Device);
        Assert.Equal(MuteMode.PushToTalk, saved.Microphone.Mode);
        Assert.Equal("MouseXButton2", saved.Microphone.Hotkey.Key);
        Assert.Equal(-1200, saved.Overlay.X);
        Assert.Equal(0.25f, saved.Sounds.Volume);
        Assert.Equal(DeviceSelection.Default, saved.Sounds.OutputDevice);
    }

    [Fact]
    public void Tells_listeners_only_about_real_changes_and_never_alters_an_old_snapshot()
    {
        using var store = new SettingsStore(path, new AppSettings());
        var before = store.Current;
        var notifications = 0;
        store.Changed += _ => notifications++;

        store.Update(s => s.General.CloseToTray = true); // already true
        Assert.Equal(0, notifications);
        Assert.Same(before, store.Current);

        store.Update(s => s.General.CloseToTray = false);
        Assert.Equal(1, notifications);
        Assert.True(before.General.CloseToTray);
        Assert.False(store.Current.General.CloseToTray);
    }

    [Fact]
    public void An_unreadable_file_reads_as_nothing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ broken");

        Assert.Null(SettingsStore.TryRead(path));
        Assert.Null(SettingsStore.TryRead(path + ".missing"));
    }

    [Fact]
    public void Settings_from_version_1_keep_their_speaker_hotkeys_and_get_silent_speaker_sounds()
    {
        var settings = SettingsStore.Deserialize(
            """
            {
              "Version": 1,
              "Sounds": { "MuteSound": "mute", "UnmuteSound": "unmute" },
              "Speakers": { "Enabled": true, "ToggleHotkey": { "Key": "F9" } }
            }
            """);

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.True(settings.Speakers.ExtraHotkeysEnabled);
        Assert.Equal("F9", settings.Speakers.ToggleHotkey.Key);
        Assert.Equal(MuteMode.Toggle, settings.Speakers.Mode);
        Assert.Equal("mute", settings.Sounds.MuteSound);
        Assert.Equal(string.Empty, settings.Speakers.Sounds.MuteSound);
        Assert.Equal(string.Empty, settings.Speakers.Sounds.UnmuteSound);
        Assert.Equal(OverlayVisibility.Never, settings.Speakers.Overlay.Visibility);

        var saved = SettingsStore.Serialize(settings);
        Assert.DoesNotContain("\"Enabled\"", saved);
        Assert.True(SettingsStore.Deserialize(saved).Speakers.ExtraHotkeysEnabled);
    }
}
