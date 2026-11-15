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
    private readonly System.Windows.Threading.Dispatcher dispatcher;

    // One icon per look, made once and reused, so toggling mute creates nothing.
    private readonly Dictionary<(MicVisual Visual, bool Light), NativeIcon> icons = [];
    private string iconSource = string.Empty;

    public TrayIconService(AppState state, DeviceController microphone, Action showWindow, Action exit)
    {
        this.state = state;
        dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

        muteItem = new MenuItem();
        muteItem.Click += (_, _) => microphone.ToggleMute();
        var openItem = new MenuItem { Header = "Open MicSwitch" };
        openItem.Click += (_, _) => showWindow();
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => exit();

        tray = new TaskbarIcon
        {
            ContextMenu = new ContextMenu { Items = { muteItem, openItem, new Separator(), exitItem } },
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(microphone.ToggleMute),
            DoubleClickCommand = new RelayCommand(showWindow)
        };

        state.MicrophoneChanged += Update;
        state.SettingsChanged += Update;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        Update();
        tray.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        state.MicrophoneChanged -= Update;
        state.SettingsChanged -= Update;
        tray.Dispose();
        ClearIcons();
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
