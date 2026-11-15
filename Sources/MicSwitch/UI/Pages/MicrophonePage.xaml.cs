using System.Windows;
using MicSwitch.UI.ViewModels;

namespace MicSwitch.UI.Pages;

internal partial class MicrophonePage
{
    private readonly MicrophoneViewModel viewModel;
    private Window? window;

    public MicrophonePage(MicrophoneViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // The level meter runs only while this page is actually on screen.
        IsVisibleChanged += (_, _) => UpdateMeter();
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
        Unloaded += (_, _) => viewModel.SetMeterActive(false);
    }

    private void UpdateMeter() =>
        viewModel.SetMeterActive(IsLoaded && IsVisible && window is { WindowState: not WindowState.Minimized });
}
