using System.Windows;
using MicSwitch.UI.ViewModels;

namespace MicSwitch.UI.Controls;

/// <summary>Device, mute and volume, shown above every page of the microphone or the speakers.</summary>
internal partial class DeviceHeader
{
    private Window? window;

    public DeviceHeader()
    {
        InitializeComponent();

        // The level meter runs only while this strip is actually on screen.
        IsVisibleChanged += (_, _) => UpdateMeter();
        DataContextChanged += (_, _) => UpdateMeter();
        Loaded += (_, _) =>
        {
            if (window == null)
            {
                window = Window.GetWindow(this);
                if (window != null)
                {
                    window.StateChanged += (_, _) => UpdateMeter();
                }
            }

            UpdateMeter();
        };
        Unloaded += (_, _) => (DataContext as DeviceHeaderViewModel)?.SetMeterActive(false);
    }

    private void UpdateMeter() =>
        (DataContext as DeviceHeaderViewModel)?.SetMeterActive(
            IsLoaded && IsVisible && window is { WindowState: not WindowState.Minimized });
}
