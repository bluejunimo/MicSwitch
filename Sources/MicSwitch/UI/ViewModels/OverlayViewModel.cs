using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MicSwitch.Settings;
using MicSwitch.Shell;

namespace MicSwitch.UI.ViewModels;

internal sealed partial class OverlayViewModel : ObservableObject
{
    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|All files|*.*";

    private readonly AppState state;
    private readonly SettingsStore settings;
    private bool syncing;

    [ObservableProperty]
    private OverlayVisibility visibility;

    [ObservableProperty]
    private bool isShown;

    [ObservableProperty]
    private double opacity;

    [ObservableProperty]
    private bool isUnlocked;

    [ObservableProperty]
    private ImageSource? micIcon;

    [ObservableProperty]
    private ImageSource? mutedIcon;

    [ObservableProperty]
    private bool hasCustomIcons;

    public OverlayViewModel(AppState state, SettingsStore settings)
    {
        this.state = state;
        this.settings = settings;
        state.SettingsChanged += Sync;
        Sync();
    }

    public IReadOnlyList<Option<OverlayVisibility>> Visibilities { get; } =
    [
        new(OverlayVisibility.Never, "Never"),
        new(OverlayVisibility.Always, "Always"),
        new(OverlayVisibility.WhenMuted, "When muted"),
        new(OverlayVisibility.WhenUnmuted, "When unmuted")
    ];

    private void Sync()
    {
        var current = state.Settings.Overlay;
        syncing = true;
        try
        {
            Visibility = current.Visibility;
            IsShown = current.Visibility != OverlayVisibility.Never;
            Opacity = Math.Round(current.Opacity * 100);
            IsUnlocked = IsShown && !current.Locked;
            MicIcon = IconRenderer.LoadImage(current.MicIconPath) ?? IconRenderer.RenderOverlay(MicVisual.Live, 64);
            MutedIcon = IconRenderer.LoadImage(current.MutedIconPath) ?? IconRenderer.RenderOverlay(MicVisual.Muted, 64);
            HasCustomIcons = current.MicIconPath != null || current.MutedIconPath != null;
        }
        finally
        {
            syncing = false;
        }
    }

    [RelayCommand]
    private void ResetPosition() => settings.Update(s =>
    {
        var defaults = new OverlaySettings();
        s.Overlay.X = null;
        s.Overlay.Y = null;
        s.Overlay.Size = defaults.Size;
    });

    [RelayCommand]
    private void ChooseMicIcon() => ChooseIcon("mic", (overlay, path) => overlay.MicIconPath = path);

    [RelayCommand]
    private void ChooseMutedIcon() => ChooseIcon("muted", (overlay, path) => overlay.MutedIconPath = path);

    [RelayCommand]
    private void ResetIcons() => settings.Update(s =>
    {
        s.Overlay.MicIconPath = null;
        s.Overlay.MutedIconPath = null;
    });

    private void ChooseIcon(string name, Action<OverlaySettings, string> assign)
    {
        var dialog = new OpenFileDialog { Title = "Choose icon", Filter = ImageFilter };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // Keep our own copy, under a new name each time so the change is noticed.
            Directory.CreateDirectory(AppInfo.IconsDirectory);
            var target = Path.Combine(AppInfo.IconsDirectory, $"{name}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(dialog.FileName)}");
            File.Copy(dialog.FileName, target, overwrite: true);
            settings.Update(s => assign(s.Overlay, target));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not use icon {Path}", dialog.FileName);
        }
    }

    partial void OnVisibilityChanged(OverlayVisibility value)
    {
        if (!syncing)
        {
            settings.Update(s =>
            {
                s.Overlay.Visibility = value;
                if (value == OverlayVisibility.Never)
                {
                    s.Overlay.Locked = true;
                }
            });
        }
    }

    partial void OnOpacityChanged(double value)
    {
        if (!syncing)
        {
            settings.Update(s => s.Overlay.Opacity = value / 100);
        }
    }

    partial void OnIsUnlockedChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s => s.Overlay.Locked = !value);
        }
    }
}
