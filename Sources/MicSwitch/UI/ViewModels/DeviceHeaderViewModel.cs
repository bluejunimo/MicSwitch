using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicSwitch.Audio;
using MicSwitch.Settings;
using MicSwitch.Shell;
using Wpf.Ui.Controls;

namespace MicSwitch.UI.ViewModels;

/// <summary>
/// The strip above a group of pages: which device, its mute state, its volume and its level.
/// One instance serves the microphone, another the speakers.
/// </summary>
internal abstract partial class DeviceHeaderViewModel : ObservableObject
{
    // While the slider is being dragged the device reports values that are already out of date;
    // showing them would pull the thumb away from the pointer.
    private static readonly TimeSpan VolumeHold = TimeSpan.FromMilliseconds(500);

    private readonly DeviceProfile profile;
    private readonly AppState state;
    private readonly SettingsStore settings;
    private readonly DeviceController controller;
    private readonly ILevelMeter meter;
    private readonly DispatcherTimer meterTimer;
    private readonly DispatcherTimer volumeHoldTimer;
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
    [NotifyPropertyChangedFor(nameof(MuteToolTip))]
    private bool isMuted;

    [ObservableProperty]
    private double volume;

    [ObservableProperty]
    private double level;

    protected DeviceHeaderViewModel(DeviceProfile profile, AppState state, AudioEngine engine, SettingsStore settings)
    {
        this.profile = profile;
        this.state = state;
        this.settings = settings;
        controller = engine.Controller(profile.Flow);
        meter = engine.MeterFor(profile.Flow);

        meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        meterTimer.Tick += (_, _) => UpdateLevel();
        volumeHoldTimer = new DispatcherTimer { Interval = VolumeHold };
        volumeHoldTimer.Tick += (_, _) =>
        {
            volumeHoldTimer.Stop();
            Sync();
        };

        state.OnDeviceChanged(profile.Flow, Sync);
        state.SettingsChanged += Sync;
        Sync();
    }

    public ObservableCollection<DeviceOption> Devices { get; } = [];

    public string Title => IsMicrophone ? "Microphone" : "Speakers";

    public string VolumeTitle => IsMicrophone ? "Input volume" : "Volume";

    public string LevelTitle => IsMicrophone ? "Input level" : "Output level";

    public SymbolRegular LiveSymbol => IsMicrophone ? SymbolRegular.Mic24 : SymbolRegular.Speaker224;

    public SymbolRegular MutedSymbol => IsMicrophone ? SymbolRegular.MicOff24 : SymbolRegular.SpeakerMute24;

    public string MuteToolTip => (IsMuted ? "Unmute " : "Mute ") + (IsMicrophone ? "microphone" : "speakers");

    private bool IsMicrophone => profile.IsMicrophone;

    private DeviceState Device => state.Device(profile.Flow);

    /// <summary>
    /// The microphone's level is measured by recording from it, which lights up the Windows
    /// "microphone in use" indicator, so the meter only runs while someone is looking at it.
    /// </summary>
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

    [RelayCommand]
    private void ToggleMute() => controller.ToggleMute();

    private void Sync()
    {
        var device = Device;
        var selection = profile.Device(state.Settings).Device;
        syncing = true;
        try
        {
            SelectedDevice = DeviceOptions.Sync(
                Devices, device.Available, selection, IsMicrophone ? "All microphones" : "All speakers", device.DefaultName);
            IsConnected = device.IsConnected;
            IsDisconnected = !device.IsConnected;
            DisconnectedTitle = selection.Kind == DeviceSelectionKind.Specific
                ? $"{selection.Name} is disconnected"
                : IsMicrophone ? "No microphone found" : "No speakers found";
            IsMuted = device.Mute ?? false;
            if (!volumeHoldTimer.IsEnabled && device.Volume is { } actual && Math.Abs(actual * 100 - Volume) >= 0.5)
            {
                Volume = Math.Round(actual * 100);
            }
        }
        finally
        {
            syncing = false;
        }

        UpdateMeterTarget();
    }

    private void UpdateMeterTarget()
    {
        // With several devices in play ("all", or two Windows defaults) show the one Windows uses by default.
        var device = Device;
        var shown = device.Bound.FirstOrDefault(x => x.Name == device.DefaultName) ?? device.Bound.FirstOrDefault();
        meter.SetTarget(meterActive ? shown?.Id : null);
    }

    private void UpdateLevel()
    {
        // Show loudness on a decibel scale (-60 dB..0 dB), falling back smoothly between peaks.
        var peak = meter.ReadPeak();
        var target = peak <= 0.001f ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
        Level = Math.Max(target, Level * 0.82);
    }

    partial void OnSelectedDeviceChanged(DeviceOption? value)
    {
        if (!syncing && value != null)
        {
            settings.Update(s => profile.Device(s).Device = value.Selection);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (!syncing)
        {
            volumeHoldTimer.Stop();
            volumeHoldTimer.Start();
            controller.SetVolume((float)(value / 100));
        }
    }
}

internal sealed class MicrophoneHeaderViewModel(AppState state, AudioEngine engine, SettingsStore settings)
    : DeviceHeaderViewModel(DeviceProfile.Microphone, state, engine, settings);

internal sealed class SpeakersHeaderViewModel(AppState state, AudioEngine engine, SettingsStore settings)
    : DeviceHeaderViewModel(DeviceProfile.Speakers, state, engine, settings);
