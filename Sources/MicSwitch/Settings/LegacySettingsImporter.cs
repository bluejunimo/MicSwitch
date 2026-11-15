using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MicSwitch.Audio;
using MicSwitch.Hotkeys;

namespace MicSwitch.Settings;

/// <summary>
/// Reads the settings of MicSwitch 1.x (config.cfg, written by the PoeEye config provider) so that an
/// existing setup carries over on first run. The file holds one entry per settings class under
/// ConfigValue.Items, each with a TypeName and its own ConfigValue.
/// </summary>
internal static partial class LegacySettingsImporter
{
    [GeneratedRegex(@"Name\s*=\s*(?<name>.*?),\s*ProductGuid")]
    private static partial Regex WaveOutName();

    public static AppSettings? TryImport(string configPath, string legacySoundsDirectory, string userSoundsDirectory, string iconsDirectory)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            var result = Import(File.ReadAllText(configPath), iconsDirectory);
            CopySounds(legacySoundsDirectory, userSoundsDirectory);
            Log.Information("Imported settings from {Path}", configPath);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not import settings from {Path}", configPath);
            return null;
        }
    }

    internal static AppSettings Import(string json, string iconsDirectory)
    {
        using var document = JsonDocument.Parse(json);
        var result = new AppSettings();
        foreach (var item in document.RootElement.GetProperty("ConfigValue").GetProperty("Items").EnumerateArray())
        {
            if (!item.TryGetProperty("ConfigValue", out var value) || value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            switch (Text(item, "TypeName")?.Split('.').Last())
            {
                case "MicSwitchConfig":
                    ImportMain(value, result);
                    break;
                case "MicSwitchHotkeyConfig":
                    ImportHotkeys(value, result.Microphone);
                    break;
                case "MicSwitchOverlayConfig":
                    ImportOverlay(value, result.Overlay, iconsDirectory);
                    break;
                case "MicSwitchVolumeControlConfig":
                    ImportSpeakers(value, result.Speakers);
                    break;
            }
        }

        return result;
    }

    private static void ImportMain(JsonElement value, AppSettings result)
    {
        result.General.StartMinimized = Bool(value, "StartMinimized") ?? false;
        result.General.CloseToTray = Bool(value, "MinimizeOnClose") ?? true;
        result.Microphone.Device = Device(value, "MicrophoneLineId") ?? DeviceSelection.All;
        result.Microphone.RestoreVolume = Bool(value, "VolumeControlEnabled") ?? false;
        result.Microphone.Volume = Number(value, "Volume") is { } volume ? (float)volume : null;
        result.Sounds.Volume = (float)Math.Clamp(Number(value, "NotificationVolume") ?? 1, 0, 1);

        if (value.TryGetProperty("Notifications", out var sounds) && sounds.ValueKind == JsonValueKind.Object)
        {
            // 1.x called the unmuted state "On".
            result.Sounds.UnmuteSound = Text(sounds, "On") ?? string.Empty;
            result.Sounds.MuteSound = Text(sounds, "Off") ?? string.Empty;
        }

        // The output device was saved as the text of an anonymous object: "{ Name = Speakers (...), ProductGuid = ... }".
        var output = WaveOutName().Match(Text(value, "OutputDeviceId") ?? string.Empty);
        if (output.Success && output.Groups["name"].Value is { Length: > 0 } name && name != "Default")
        {
            result.Sounds.OutputDevice = new DeviceSelection(DeviceSelectionKind.Specific, null, name);
        }
    }

    private static void ImportHotkeys(JsonElement value, MicrophoneSettings result)
    {
        result.Mode = Text(value, "MuteMode") switch
        {
            "PushToTalk" or "1" => MuteMode.PushToTalk,
            "PushToMute" or "2" => MuteMode.PushToMute,
            _ => MuteMode.Toggle
        };
        result.StartupState = Text(value, "InitialMicrophoneState") switch
        {
            "Mute" or "1" => StartupMicState.Mute,
            "Unmute" or "2" => StartupMicState.Unmute,
            _ => StartupMicState.Keep
        };
        result.ExtraHotkeysEnabled = Bool(value, "EnableAdvancedHotkeys") ?? false;
        result.Hotkey = Hotkey(value, "Hotkey");
        result.ToggleHotkey = Hotkey(value, "HotkeyForToggle");
        result.MuteHotkey = Hotkey(value, "HotkeyForMute");
        result.UnmuteHotkey = Hotkey(value, "HotkeyForUnmute");
        result.PushToTalkHotkey = Hotkey(value, "HotkeyForPushToTalk");
        result.PushToMuteHotkey = Hotkey(value, "HotkeyForPushToMute");
    }

    private static void ImportOverlay(JsonElement value, OverlaySettings result, string iconsDirectory)
    {
        result.Visibility = Text(value, "OverlayVisibilityMode") switch
        {
            "Never" or "1" => OverlayVisibility.Never,
            "WhenMuted" or "2" => OverlayVisibility.WhenMuted,
            "WhenUnmuted" or "3" => OverlayVisibility.WhenUnmuted,
            _ => OverlayVisibility.Always
        };

        if (Number(value, "OverlayOpacity") is { } opacity and > 0)
        {
            result.Opacity = Math.Clamp(opacity, 0.1, 1);
        }

        // "x, y, width, height" in screen pixels.
        var bounds = (Text(value, "OverlayBounds") ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : (int?)null)
            .ToArray();
        if (bounds.Length == 4 && bounds.All(x => x != null) && bounds[2] > 0)
        {
            result.X = bounds[0];
            result.Y = bounds[1];
            result.Size = bounds[2]!.Value;
        }

        result.MicIconPath = SaveIcon(value, "MicrophoneIcon", iconsDirectory, "mic");
        result.MutedIconPath = SaveIcon(value, "MutedMicrophoneIcon", iconsDirectory, "muted");
    }

    private static void ImportSpeakers(JsonElement value, SpeakerSettings result)
    {
        result.Enabled = Bool(value, "IsEnabled") ?? false;
        result.Device = Device(value, "DeviceId") ?? DeviceSelection.All;
        result.ToggleHotkey = Hotkey(value, "HotkeyForToggle");
        result.MuteHotkey = Hotkey(value, "HotkeyForMute");
        result.UnmuteHotkey = Hotkey(value, "HotkeyForUnmute");
        result.VolumeUpHotkey = Hotkey(value, "HotkeyForVolumeUp");
        result.VolumeDownHotkey = Hotkey(value, "HotkeyForVolumeDown");
    }

    private static HotkeySetting Hotkey(JsonElement parent, string name)
    {
        var result = new HotkeySetting();
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        result.Key = Normalize(Text(value, "Key"));
        result.AltKey = Normalize(Text(value, "AlternativeKey"));
        result.Suppress = Bool(value, "Suppress") ?? true;
        result.IgnoreModifiers = Bool(value, "IgnoreModifiers") ?? false;
        return result;

        static string Normalize(string? text)
        {
            var gesture = HotkeyGesture.Parse(text);
            return gesture.IsEmpty ? string.Empty : gesture.ToString();
        }
    }

    private static DeviceSelection? Device(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = Text(value, "LineId");
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return id == "all" ? DeviceSelection.All : DeviceSelection.Specific(id, Text(value, "Name") ?? string.Empty);
    }

    private static string? SaveIcon(JsonElement parent, string name, string iconsDirectory, string fileName)
    {
        if (Text(parent, name) is not { Length: > 0 } base64)
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            var extension = bytes switch
            {
                [0x89, 0x50, 0x4E, 0x47, ..] => ".png",
                [0x47, 0x49, 0x46, ..] => ".gif",
                [0xFF, 0xD8, ..] => ".jpg",
                [0x42, 0x4D, ..] => ".bmp",
                [0x00, 0x00, 0x01, 0x00, ..] => ".ico",
                _ => ".img"
            };
            Directory.CreateDirectory(iconsDirectory);
            var path = Path.Combine(iconsDirectory, fileName + extension);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not import icon {Name}", name);
            return null;
        }
    }

    private static void CopySounds(string from, string to)
    {
        if (!Directory.Exists(from))
        {
            return;
        }

        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    private static bool? Bool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static double? Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
