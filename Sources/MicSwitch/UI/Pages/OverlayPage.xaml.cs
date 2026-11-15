using MicSwitch.UI.ViewModels;

namespace MicSwitch.UI.Pages;

internal partial class OverlayPage
{
    public OverlayPage(OverlayViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
