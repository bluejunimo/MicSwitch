using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MicSwitch.Settings;
using MicSwitch.Shell;

namespace MicSwitch.UI.ViewModels;

/// <summary>The microphone's overlay icon. <see cref="SpeakerOverlayViewModel"/> is the same for the speakers.</summary>
internal partial class OverlayViewModel : ObservableObject
{
    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|All files|*.*";

    private readonly DeviceProfile profile;
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
        : this(DeviceProfile.Microphone, state, settings)
    {
    }

    protected OverlayViewModel(DeviceProfile profile, AppState state, SettingsStore settings)
    {
        this.profile = profile;
        this.state = state;
        this.settings = settings;
        state.SettingsChanged += Sync;
        Sync();
    }

    public string Description => profile.IsMicrophone
        ? "A microphone icon that stays on top of other windows."
        : "A speaker icon that stays on top of other windows.";

    public string IconsDescription => profile.IsMicrophone ? "Used by the overlay and the tray icon." : "Used by the overlay.";

    public IReadOnlyList<Option<OverlayVisibility>> Visibilities { get; } =
    [
        new(OverlayVisibility.Never, "Never"),
        new(OverlayVisibility.Always, "Always"),
        new(OverlayVisibility.WhenMuted, "When muted"),
        new(OverlayVisibility.WhenUnmuted, "When unmuted")
    ];

    private void Sync()
    {
        var current = profile.Overlay(state.Settings);
        var speakers = !profile.IsMicrophone;
        syncing = true;
        try
        {
            Visibility = current.Visibility;
            IsShown = current.Visibility != OverlayVisibility.Never;
            Opacity = Math.Round(current.Opacity * 100);
            IsUnlocked = IsShown && !current.Locked;
            MicIcon = IconRenderer.LoadImage(current.MicIconPath) ?? IconRenderer.RenderOverlay(MicVisual.Live, 64, speakers);
            MutedIcon = IconRenderer.LoadImage(current.MutedIconPath) ?? IconRenderer.RenderOverlay(MicVisual.Muted, 64, speakers);
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
        var overlay = profile.Overlay(s);
        overlay.X = null;
        overlay.Y = null;
        overlay.Size = new OverlaySettings().Size;
    });

    [RelayCommand]
    private void ChooseMicIcon() => ChooseIcon(profile.IsMicrophone ? "mic" : "speaker", (overlay, path) => overlay.MicIconPath = path);

    [RelayCommand]
    private void ChooseMutedIcon() => ChooseIcon(profile.IsMicrophone ? "muted" : "speaker-muted", (overlay, path) => overlay.MutedIconPath = path);

    [RelayCommand]
    private void ResetIcons() => settings.Update(s =>
    {
        profile.Overlay(s).MicIconPath = null;
        profile.Overlay(s).MutedIconPath = null;
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
            settings.Update(s => assign(profile.Overlay(s), target));
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
                profile.Overlay(s).Visibility = value;
                if (value == OverlayVisibility.Never)
                {
                    profile.Overlay(s).Locked = true;
                }
            });
        }
    }

    partial void OnOpacityChanged(double value)
    {
        if (!syncing)
        {
            settings.Update(s => profile.Overlay(s).Opacity = value / 100);
        }
    }

    partial void OnIsUnlockedChanged(bool value)
    {
        if (!syncing)
        {
            settings.Update(s => profile.Overlay(s).Locked = !value);
        }
    }
}

internal sealed class SpeakerOverlayViewModel(AppState state, SettingsStore settings)
    : OverlayViewModel(DeviceProfile.Speakers, state, settings);
