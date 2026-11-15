using System.ComponentModel;
using System.Windows;
using MicSwitch.Settings;
using MicSwitch.UI.Pages;
using MicSwitch.UI.ViewModels;

namespace MicSwitch.UI;

internal partial class MainWindow
{
    // Below this width the navigation pane collapses to icons, like Windows Settings.
    private const double CompactBelowWidth = 760;

    private readonly SettingsStore settings;

    public MainWindow(
        SettingsStore settings,
        IServiceProvider services,
        MicrophoneHeaderViewModel microphone,
        SpeakersHeaderViewModel speakers)
    {
        this.settings = settings;
        InitializeComponent();
        MicrophoneHeader.DataContext = microphone;
        SpeakersHeader.DataContext = speakers;

        var general = settings.Current.General;
        if (general is { WindowWidth: { } width, WindowHeight: { } height })
        {
            Width = Math.Max(MinWidth, width);
            Height = Math.Max(MinHeight, height);
        }

        Navigation.SetServiceProvider(services);
        Navigation.Navigated += (_, e) => ShowHeaderFor(e.Page);
        Navigation.PaneOpened += (_, _) => ShowGroupNames(true);
        Navigation.PaneClosed += (_, _) => ShowGroupNames(false);
        Loaded += (_, _) =>
        {
            Navigation.Navigate(StartPage());

            // The button that collapses the pane comes without a name or tooltip.
            if (Navigation.Template?.FindName("PART_ToggleButton", Navigation) is FrameworkElement toggle)
            {
                toggle.ToolTip = "Open or close navigation";
                System.Windows.Automation.AutomationProperties.SetName(toggle, "Open or close navigation");
            }
        };
        SizeChanged += (_, e) =>
        {
            var compact = e.NewSize.Width < CompactBelowWidth;
            if (compact != isCompact)
            {
                isCompact = compact;
                Navigation.IsPaneOpen = !compact;
            }
        };
    }

    private bool? isCompact;

    // MICSWITCH_PAGE picks the page to open on (used for testing).
    private static Type StartPage() => Environment.GetEnvironmentVariable("MICSWITCH_PAGE")?.ToLowerInvariant() switch
    {
        "sounds" => typeof(SoundsPage),
        "overlay" => typeof(OverlayPage),
        "speakers" => typeof(SpeakerHotkeysPage),
        "speaker-sounds" => typeof(SpeakerSoundsPage),
        "speaker-overlay" => typeof(SpeakerOverlayPage),
        "general" => typeof(GeneralPage),
        _ => typeof(HotkeysPage)
    };

    // Collapsed to icons there is no room for the group names; a line separates the groups instead.
    private void ShowGroupNames(bool show)
    {
        MicrophoneGroup.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SpeakersGroup.Visibility = MicrophoneGroup.Visibility;
        GroupSeparator.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The title and device strip above a page say which device its settings belong to.</summary>
    private void ShowHeaderFor(object page)
    {
        // The speaker pages derive from the microphone's, so they are matched first.
        var (section, title) = page switch
        {
            SpeakerHotkeysPage => ("Speakers", "Hotkeys"),
            SpeakerSoundsPage => ("Speakers", "Sounds"),
            SpeakerOverlayPage => ("Speakers", "Overlay"),
            HotkeysPage => ("Microphone", "Hotkeys"),
            SoundsPage => ("Microphone", "Sounds"),
            OverlayPage => ("Microphone", "Overlay"),
            _ => (null, "General")
        };

        SectionTitle.Text = section;
        SectionTitle.Visibility = section == null ? Visibility.Collapsed : Visibility.Visible;
        TitleSeparator.Visibility = SectionTitle.Visibility;
        PageTitle.Text = title;
        MicrophoneHeader.Visibility = section == "Microphone" ? Visibility.Visible : Visibility.Collapsed;
        SpeakersHeader.Visibility = section == "Speakers" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Set when the application is shutting down, so closing isn't turned into hiding.</summary>
    public bool AllowClose { get; set; }

    /// <summary>The user closed the window and "close to tray" is off.</summary>
    public event Action? ExitRequested;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            settings.Update(s =>
            {
                s.General.WindowWidth = Math.Round(ActualWidth);
                s.General.WindowHeight = Math.Round(ActualHeight);
            });
        }

        if (!AllowClose)
        {
            e.Cancel = true;
            if (settings.Current.General.CloseToTray)
            {
                Hide();
            }
            else
            {
                // Exiting closes this window again, which isn't allowed from inside its own closing.
                Dispatcher.BeginInvoke(() => ExitRequested?.Invoke());
            }
        }

        base.OnClosing(e);
    }
}
