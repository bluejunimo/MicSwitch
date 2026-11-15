# MicSwitch

Mute and unmute your microphone system-wide with a hotkey, so it works the same in every chat app.

This is a rebuild of [MicSwitch by Xab3r](https://github.com/iXab3r/MicSwitch), which is no longer maintained. Version 2 keeps the features, fixes long-standing device and sound bugs, and has a Windows 11 style interface.

## Features
- Toggle, push-to-talk and push-to-mute, on keyboard keys or mouse buttons
- One microphone, the Windows default, or all microphones at once
- A sound when the microphone is muted or unmuted, including your own sound files
- An always-on-top overlay icon and a tray icon that show the mute state
- Optional hotkeys for speaker mute and volume
- Light and dark theme, following Windows

## What changed in version 2
- A microphone is found again after it is unplugged, moved to another USB port, or renumbered by a Windows update.
- Sounds no longer pile up when the hotkey is pressed many times quickly; the newest sound replaces the one playing.
- The sound output list shows the real devices and remembers the choice.
- It runs without administrator rights. The one consequence: hotkeys don't work while a program running as administrator has focus.
- It no longer depends on the PoeEye library or on git submodules.

## Install
Download `MicSwitch-win-Setup.exe` from the releases page of this repository. Installed copies update themselves from the same place.

### Coming from MicSwitch 1.x
1. Exit the old MicSwitch (tray icon, Exit) and run the new one once. It imports your microphone, hotkeys, sounds and overlay settings.
2. Uninstall the old version. Until you do, both will react to the same hotkey.

Settings live in `%APPDATA%\MicSwitch\settings.json`, logs in `%APPDATA%\MicSwitch\logs`.

## Build
Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build Sources/MicSwitch.slnx
dotnet test Sources/MicSwitch.slnx
dotnet run --project Sources/MicSwitch
```

A build run this way has no update check; that is only switched on in releases.

## Release
Push a tag such as `v2.0.1`. The [release workflow](.github/workflows/release.yml) tests, builds the installer with [Velopack](https://velopack.io) and publishes it as a GitHub release of this repository.
