using MicSwitch.UI.ViewModels;

namespace MicSwitch.UI.Pages;

internal partial class HotkeysPage
{
    public HotkeysPage(HotkeysViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
