using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MicSwitch.Settings;
using Wpf.Ui.Controls;
using static MicSwitch.Infrastructure.NativeMethods;

namespace MicSwitch.Shell;

/// <summary>
/// A small always-on-top microphone icon. Locked, it lets clicks through to whatever is underneath;
/// unlocked, it can be dragged and resized. Position and size are kept in screen pixels.
/// </summary>
internal partial class OverlayWindow : Window
{
    private const int MinSize = 24;
    private const int MaxSize = 400;
    private const int DefaultMargin = 48;
    private static readonly TimeSpan VolumeIndicatorTime = TimeSpan.FromSeconds(2);

    private readonly AppState state;
    private readonly SettingsStore settings;
    private readonly DispatcherTimer volumeTimer;
    private readonly WinEventProc foregroundChanged;
    private IntPtr handle;
    private IntPtr foregroundHook;
    private (float? Volume, bool? Mute) lastSpeakers;
    private (MicVisual Visual, string Source) shownIcon = (MicVisual.Disconnected, "?");
    private double resizeSize;

    public OverlayWindow(AppState state, SettingsStore settings)
    {
        this.state = state;
        this.settings = settings;
        InitializeComponent();

        foregroundChanged = (_, _, _, _, _, _, _) => KeepOnTop();
        volumeTimer = new DispatcherTimer { Interval = VolumeIndicatorTime };
        volumeTimer.Tick += (_, _) => ShowVolumeIndicator(false);
        lastSpeakers = (state.Speakers.Volume, state.Speakers.Mute);

        EditFrame.MouseLeftButtonDown += OnFrameMouseDown;
        state.MicrophoneChanged += Update;
        state.SettingsChanged += Update;
        state.SpeakersChanged += OnSpeakersChanged;
        Update();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        handle = new WindowInteropHelper(this).Handle;
        ApplyWindowStyle();
        ApplyBounds();

        // Full-screen and other topmost windows can end up above us; step back on top whenever focus moves.
        foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, foregroundChanged, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(foregroundHook);
        }

        state.MicrophoneChanged -= Update;
        state.SettingsChanged -= Update;
        state.SpeakersChanged -= OnSpeakersChanged;
        base.OnClosed(e);
    }

    private OverlaySettings Overlay => state.Settings.Overlay;

    private void Update()
    {
        var overlay = Overlay;
        var visual = TrayIconService.Describe(state.Microphone);
        var editing = !overlay.Locked && overlay.Visibility != OverlayVisibility.Never;
        var visible = editing || overlay.Visibility switch
        {
            OverlayVisibility.Always => true,
            OverlayVisibility.WhenMuted => visual == MicVisual.Muted,
            OverlayVisibility.WhenUnmuted => visual == MicVisual.Live,
            _ => false
        };

        var source = overlay.MicIconPath + "|" + overlay.MutedIconPath;
        if (shownIcon != (visual, source))
        {
            shownIcon = (visual, source);
            var custom = IconRenderer.LoadImage(visual == MicVisual.Muted ? overlay.MutedIconPath : overlay.MicIconPath);
            MicImage.Source = custom ?? IconRenderer.RenderOverlay(visual, 256);
        }

        MicImage.Opacity = overlay.Opacity;
        VolumeIndicator.Opacity = overlay.Opacity;
        EditFrame.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ResizeGrip.Visibility = EditFrame.Visibility;

        if (visible && !IsVisible)
        {
            Show();
        }
        else if (!visible && IsVisible)
        {
            Hide();
        }

        if (handle != IntPtr.Zero)
        {
            ApplyWindowStyle();
            ApplyBounds();
        }
    }

    private void OnSpeakersChanged()
    {
        var current = (state.Speakers.Volume, state.Speakers.Mute);
        var changed = current != lastSpeakers && lastSpeakers.Volume != null && current.Volume != null;
        lastSpeakers = current;
        if (!changed || !state.Settings.Speakers.Enabled || !IsVisible)
        {
            return;
        }

        VolumeRing.Progress = (current.Volume ?? 0) * 100;
        VolumeIcon.Symbol = current.Mute == true ? SymbolRegular.SpeakerMute24 : SymbolRegular.Speaker224;
        ShowVolumeIndicator(true);
        volumeTimer.Stop();
        volumeTimer.Start();
    }

    private void ShowVolumeIndicator(bool show)
    {
        volumeTimer.Stop();
        VolumeIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MicImage.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyWindowStyle()
    {
        var style = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
        style = Overlay.Locked ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style));
    }

    private void ApplyBounds()
    {
        var overlay = Overlay;
        var size = Math.Clamp(overlay.Size, MinSize, MaxSize);
        var bounds = overlay is { X: { } x, Y: { } y }
            ? new RECT { Left = x, Top = y, Right = x + size, Bottom = y + size }
            : DefaultBounds(size);

        if (MonitorFromRect(ref bounds, 0 /* MONITOR_DEFAULTTONULL */) == IntPtr.Zero)
        {
            // Saved on a monitor that is no longer there.
            bounds = DefaultBounds(size);
        }

        if (GetWindowRect(handle, out var actual) &&
            actual.Left == bounds.Left && actual.Top == bounds.Top &&
            actual.Right == bounds.Right && actual.Bottom == bounds.Bottom)
        {
            return;
        }

        SetWindowPos(handle, HWND_TOPMOST, bounds.Left, bounds.Top, size, size, SWP_NOACTIVATE);
    }

    private RECT DefaultBounds(int size)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var area = SystemParameters.WorkArea;
        var right = (int)(area.Right * scale) - DefaultMargin;
        var bottom = (int)(area.Bottom * scale) - DefaultMargin;
        return new RECT { Left = right - size, Top = bottom - size, Right = right, Bottom = bottom };
    }

    private void KeepOnTop()
    {
        if (IsVisible && handle != IntPtr.Zero)
        {
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
    }

    private void OnFrameMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            settings.Update(s => s.Overlay.Locked = true);
            return;
        }

        DragMove(); // returns when the button is released
        SaveBounds();
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (!GetWindowRect(handle, out var rect))
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (resizeSize == 0)
        {
            resizeSize = rect.Right - rect.Left;
        }

        // Stays square: follow whichever direction the pointer moved further.
        var delta = Math.Abs(e.HorizontalChange) > Math.Abs(e.VerticalChange) ? e.HorizontalChange : e.VerticalChange;
        resizeSize = Math.Clamp(resizeSize + delta * scale, MinSize, MaxSize);
        var size = (int)Math.Round(resizeSize);
        SetWindowPos(handle, IntPtr.Zero, 0, 0, size, size, SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | 0x0004 /* SWP_NOZORDER */);
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        resizeSize = 0;
        SaveBounds();
    }

    private void SaveBounds()
    {
        if (GetWindowRect(handle, out var rect))
        {
            settings.Update(s =>
            {
                s.Overlay.X = rect.Left;
                s.Overlay.Y = rect.Top;
                s.Overlay.Size = Math.Clamp(rect.Right - rect.Left, MinSize, MaxSize);
            });
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
}
