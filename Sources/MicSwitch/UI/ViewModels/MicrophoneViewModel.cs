using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MicSwitch.Audio;
using MicSwitch.Settings;
using MicSwitch.Shell;

namespace MicSwitch.UI.ViewModels;

internal sealed partial class MicrophoneViewModel : ObservableObject
{
    private readonly AppState state;
    private readonly AudioEngine engine;
    private readonly SettingsStore settings;
    private readonly DispatcherTimer meterTimer;
    private bool syncing;
    private bool meterActive;

    [ObservableProperty]
    private DeviceOption? selectedDevice;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private bool isDisconnected;

    [ObservableProperty]
    private string disconnectedTitle = string.Empty;

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private double volume;

    [ObservableProperty]
    private bool restoreVolume;

    [ObservableProperty]
    private StartupMicState startupState;

    [ObservableProperty]
    private bool isStartupStateEnabled;

    [ObservableProperty]
    private double level;

    public MicrophoneViewModel(AppState state, AudioEngine engine, SettingsStore settings)
    {
        this.state = state;
        this.engine = engine;
        this.settings = settings;
        meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        meterTimer.Tick += (_, _) => UpdateLevel();

        state.MicrophoneChanged += Sync;
        state.SettingsChanged += Sync;
        Sync();
    }

    public ObservableCollection<DeviceOption> Devices { get; } = [];

    public IReadOnlyList<Option<StartupMicState>> StartupStates { get; } =
    [
        new(StartupMicState.Keep, "Keep as is"),
        new(StartupMicState.Mute, "Mute"),
        new(StartupMicState.Unmute, "Unmute")
    ];

    /// <summary>The level meter records from the microphone, so it only runs while someone is looking at it.</summary>
    public void SetMeterActive(bool active)
    {
        if (meterActive == active)
        {
            return;
        }

        meterActive = active;
        if (active)
        {
            meterTimer.Start();
        }
        else
        {
            meterTimer.Stop();
            Level = 0;
        }

        UpdateMeterTarget();
    }

    private void Sync()
    {
        var microphone = state.Microphone;
        var current = state.Settings.Microphone;
        syncing = true;
        try
        {
            SelectedDevice = DeviceOptions.Sync(Devices, microphone.Available, current.Device, "All microphones");
            IsConnected = microphone.IsConnected;
            IsDisconnected = !microphone.IsConnected;
            DisconnectedTitle = current.Device.Kind switch
            {
                DeviceSelectionKind.Specific => $"{current.Device.Name} is disconnected",
                _ => "No microphone found"
            };
            IsMuted = microphone.Mute ?? false;
            if (microphone.Volume is { } actual && Math.Abs(actual * 100 - Volume) >= 0.5)
            {
                Volume = Math.Round(actual * 100);
            }

            RestoreVolume = current.RestoreVolume;
            StartupState = current.StartupState;
            IsStartupStateEnabled = current.Mode == MuteMode.Toggle;
        }
        finally
        {
            syncing = false;
        }

        UpdateMeterTarget();
    }

    private void UpdateMeterTarget() =>
        engine.Meter.SetTarget(meterActive ? state.Microphone.Bound.FirstOrDefault()?.Id : null);

    private void UpdateLevel()
    {
        // Show loudness on a decibel scale (-60 dB..0 dB), falling back smoothly between peaks.
        var peak = engine.Meter.ReadPeak();
        var target = peak <= 0.001f ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
        Level = Math.Max(target, Level * 0.82);
    }

    partial void OnSelectedDeviceChanged(DeviceOption? value)
    {
        if (!syncing && value != null)
        {
            settings.Update(s => s.Microphone.Device = value.Selection);
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (!syncing)
        {
            engine.Microphone.SetMute(value);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (!syncing)
        {
            engine.Microphone.SetVolume((float)(value / 100));
        }
    }

    partial void OnRestoreVolumeChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s =>
            {
                s.Microphone.RestoreVolume = value;
                s.Microphone.Volume = value ? state.Microphone.Volume : null;
            });
        }
    }

    partial void OnStartupStateChanged(StartupMicState value)
    {
        if (!syncing)
        {
            settings.Update(s => s.Microphone.StartupState = value);
        }
    }
}
