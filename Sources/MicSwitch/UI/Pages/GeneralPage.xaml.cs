using MicSwitch.UI.ViewModels;

namespace MicSwitch.UI.Pages;

internal partial class GeneralPage
{
    public GeneralPage(GeneralViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
