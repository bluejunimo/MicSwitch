using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.Win32;
using MicSwitch.Audio;

namespace MicSwitch.Shell;

/// <summary>The notification-area icon: shows the mute state, click to toggle, double-click to open.</summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly AppState state;
    private readonly TaskbarIcon tray;
    private readonly MenuItem muteItem;
    private readonly MenuItem updateItem;
    private readonly UpdateService updates;
    private string? announcedVersion;
    private readonly System.Windows.Threading.Dispatcher dispatcher;

    // One icon per look, made once and reused, so toggling mute creates nothing.
    private readonly Dictionary<(MicVisual Visual, bool Light), NativeIcon> icons = [];
    private string iconSource = string.Empty;

    public TrayIconService(AppState state, DeviceController microphone, UpdateService updates, Action showWindow, Action exit)
    {
        this.state = state;
        this.updates = updates;
        dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

        muteItem = new MenuItem();
        muteItem.Click += (_, _) => microphone.ToggleMute();
        var openItem = new MenuItem { Header = "Open MicSwitch" };
        openItem.Click += (_, _) => showWindow();
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => exit();
        updateItem = new MenuItem { Visibility = System.Windows.Visibility.Collapsed };
        updateItem.Click += (_, _) => _ = updates.InstallAsync();

        tray = new TaskbarIcon
        {
            ContextMenu = new ContextMenu { Items = { muteItem, openItem, updateItem, new Separator(), exitItem } },
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(microphone.ToggleMute),
            DoubleClickCommand = new RelayCommand(showWindow)
        };

        state.MicrophoneChanged += Update;
        state.SettingsChanged += Update;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        updates.StatusChanged += OnUpdateStatusChanged;
        tray.TrayBalloonTipClicked += (_, _) => _ = updates.InstallAsync();

        // Windows keeps the tooltip an icon is created with as its name (screen readers announce it
        // before the current tooltip), so the icon is created as "MicSwitch" and given its status after.
        Update();
        tray.ToolTipText = AppInfo.Name;
        tray.ForceCreate(enablesEfficiencyMode: false);
        Update();
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        state.MicrophoneChanged -= Update;
        state.SettingsChanged -= Update;
        updates.StatusChanged -= OnUpdateStatusChanged;
        tray.Dispose();
        ClearIcons();
    }

    private void OnUpdateStatusChanged() => dispatcher.BeginInvoke(ShowUpdate);

    // MicSwitch mostly lives in the tray, so that is where a waiting update is announced: once, as a
    // notification that installs it when clicked, and for as long as it waits in the menu.
    private void ShowUpdate()
    {
        var ready = updates.Status == UpdateStatus.ReadyToRestart;
        updateItem.Visibility = ready ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        updateItem.Header = $"Restart to update to {updates.NewVersion}";
        if (ready && updates.FoundInBackground && announcedVersion != updates.NewVersion)
        {
            announcedVersion = updates.NewVersion;
            try
            {
                tray.ShowNotification(
                    $"MicSwitch {updates.NewVersion} is ready",
                    "Click here to restart MicSwitch and install it. Otherwise it is installed the next time MicSwitch starts.",
                    H.NotifyIcon.Core.NotificationIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not show the update notification");
            }
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => dispatcher.BeginInvoke(Update);

    private void Update()
    {
        var microphone = state.Microphone;
        var visual = Describe(microphone);
        var overlay = state.Settings.Overlay;

        // Custom images replace the built-in glyphs everywhere, so a change of image invalidates the lot.
        var source = overlay.MicIconPath + "|" + overlay.MutedIconPath;
        List<NativeIcon> stale = [];
        if (source != iconSource)
        {
            stale = icons.Values.ToList();
            icons.Clear();
            iconSource = source;
        }

        var key = (visual, IconRenderer.IsTaskbarLight());
        if (!icons.TryGetValue(key, out var icon))
        {
            icon = CreateIcon(visual, key.Item2, overlay.MicIconPath, overlay.MutedIconPath);
            icons[key] = icon;
        }

        tray.Icon = icon.Icon;
        stale.ForEach(x => x.Dispose());

        var name = microphone.Selection.Kind == DeviceSelectionKind.All
            ? "All microphones"
            : microphone.Bound.FirstOrDefault()?.Name ?? microphone.Selection.Name ?? "Microphone";
        var status = visual switch
        {
            MicVisual.Muted => "muted",
            MicVisual.Live => "on",
            _ => "disconnected"
        };
        var tooltip = $"{name}: {status}";
        tray.ToolTipText = tooltip.Length > 120 ? tooltip[..120] : tooltip;

        muteItem.Header = visual == MicVisual.Muted ? "Unmute" : "Mute";
        muteItem.IsEnabled = visual != MicVisual.Disconnected;
    }

    private static NativeIcon CreateIcon(MicVisual visual, bool lightTaskbar, string? micIconPath, string? mutedIconPath)
    {
        var size = IconRenderer.TrayIconSize;
        var custom = IconRenderer.LoadImage(visual == MicVisual.Muted ? mutedIconPath : micIconPath);
        var bitmap = custom != null
            ? IconRenderer.RenderImage(custom, size, visual == MicVisual.Disconnected ? 0.45 : 1)
            : IconRenderer.RenderTray(visual, lightTaskbar, size);
        return IconRenderer.ToIcon(bitmap);
    }

    private void ClearIcons()
    {
        foreach (var icon in icons.Values)
        {
            icon.Dispose();
        }

        icons.Clear();
    }

    internal static MicVisual Describe(DeviceState microphone) =>
        !microphone.IsConnected ? MicVisual.Disconnected : microphone.Mute == true ? MicVisual.Muted : MicVisual.Live;
}
