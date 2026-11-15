using MicSwitch.UI.ViewModels;

namespace MicSwitch.UI.Pages;

internal partial class SoundsPage
{
    public SoundsPage(SoundsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
