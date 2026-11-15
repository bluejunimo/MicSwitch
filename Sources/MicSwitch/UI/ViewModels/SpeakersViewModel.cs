using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MicSwitch.Audio;
using MicSwitch.Settings;
using MicSwitch.Shell;

namespace MicSwitch.UI.ViewModels;

internal sealed partial class SpeakersViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly AudioEngine engine;
    private readonly SettingsStore settings;
    private bool syncing;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private DeviceOption? selectedDevice;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private double volume;

    public SpeakersViewModel(AppState state, AudioEngine engine, SettingsStore settings)
    {
        this.state = state;
        this.engine = engine;
        this.settings = settings;
        Hotkeys =
        [
            new HotkeyEditorViewModel("Toggle mute", settings, s => s.Speakers.ToggleHotkey),
            new HotkeyEditorViewModel("Mute", settings, s => s.Speakers.MuteHotkey),
            new HotkeyEditorViewModel("Unmute", settings, s => s.Speakers.UnmuteHotkey),
            new HotkeyEditorViewModel("Volume up", settings, s => s.Speakers.VolumeUpHotkey),
            new HotkeyEditorViewModel("Volume down", settings, s => s.Speakers.VolumeDownHotkey)
        ];

        state.SpeakersChanged += Sync;
        state.SettingsChanged += Sync;
        Sync();
    }

    public ObservableCollection<DeviceOption> Devices { get; } = [];

    public IReadOnlyList<HotkeyEditorViewModel> Hotkeys { get; }

    private void Sync()
    {
        var speakers = state.Speakers;
        var current = state.Settings;
        syncing = true;
        try
        {
            IsEnabled = current.Speakers.Enabled;
            SelectedDevice = DeviceOptions.Sync(Devices, speakers.Available, current.Speakers.Device, "All speakers");
            IsConnected = speakers.IsConnected;
            IsMuted = speakers.Mute ?? false;
            if (speakers.Volume is { } actual && Math.Abs(actual * 100 - Volume) >= 0.5)
            {
                Volume = Math.Round(actual * 100);
            }
        }
        finally
        {
            syncing = false;
        }

        foreach (var hotkey in Hotkeys)
        {
            hotkey.Load(current);
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s => s.Speakers.Enabled = value);
        }
    }

    partial void OnSelectedDeviceChanged(DeviceOption? value)
    {
        if (!syncing && value != null)
        {
            settings.Update(s => s.Speakers.Device = value.Selection);
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (!syncing)
        {
            engine.Speakers.SetMute(value);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (!syncing)
        {
            engine.Speakers.SetVolume((float)(value / 100));
        }
    }
}
