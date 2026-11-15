using System.ComponentModel;
using System.Windows;
using MicSwitch.Settings;
using MicSwitch.UI.Pages;
using Wpf.Ui.Appearance;

namespace MicSwitch.UI;

internal partial class MainWindow
{
    // Below this width the navigation pane collapses to icons, like Windows Settings.
    private const double CompactBelowWidth = 760;

    private readonly SettingsStore settings;

    public MainWindow(SettingsStore settings, IServiceProvider services)
    {
        this.settings = settings;
        InitializeComponent();

        var general = settings.Current.General;
        if (general is { WindowWidth: { } width, WindowHeight: { } height })
        {
            Width = Math.Max(MinWidth, width);
            Height = Math.Max(MinHeight, height);
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MICSWITCH_THEME")))
        {
            SystemThemeWatcher.Watch(this);
        }
        Navigation.SetServiceProvider(services);
        Loaded += (_, _) => Navigation.Navigate(StartPage());
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
        "hotkeys" => typeof(HotkeysPage),
        "sounds" => typeof(SoundsPage),
        "overlay" => typeof(OverlayPage),
        "speakers" => typeof(SpeakersPage),
        "general" => typeof(GeneralPage),
        _ => typeof(MicrophonePage)
    };

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
