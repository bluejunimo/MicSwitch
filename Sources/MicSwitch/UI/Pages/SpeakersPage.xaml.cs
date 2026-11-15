using MicSwitch.UI.ViewModels;

namespace MicSwitch.UI.Pages;

internal partial class SpeakersPage
{
    public SpeakersPage(SpeakersViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
