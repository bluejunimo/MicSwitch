using MicSwitch.UI.ViewModels;

namespace MicSwitch.UI.Pages;

// The speakers have the same pages as the microphone, filled from their own settings.

internal sealed class SpeakerHotkeysPage(SpeakerHotkeysViewModel viewModel) : HotkeysPage(viewModel);

internal sealed class SpeakerSoundsPage(SpeakerSoundsViewModel viewModel) : SoundsPage(viewModel);

internal sealed class SpeakerOverlayPage(SpeakerOverlayViewModel viewModel) : OverlayPage(viewModel);
