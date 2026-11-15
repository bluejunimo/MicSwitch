using CommunityToolkit.Mvvm.ComponentModel;
using MicSwitch.Hotkeys;
using MicSwitch.Settings;

namespace MicSwitch.UI.ViewModels;

/// <summary>One hotkey: a key, an alternative key, and how they behave.</summary>
internal sealed partial class HotkeyEditorViewModel : ObservableObject
{
    private readonly SettingsStore settings;
    private readonly Func<AppSettings, HotkeySetting> select;
    private bool syncing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIgnoreModifiers))]
    private HotkeyGesture key;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIgnoreModifiers))]
    private HotkeyGesture altKey;

    [ObservableProperty]
    private bool suppress;

    [ObservableProperty]
    private bool ignoreModifiers;

    public HotkeyEditorViewModel(string label, SettingsStore settings, Func<AppSettings, HotkeySetting> select)
    {
        Label = label;
        this.settings = settings;
        this.select = select;
        Load(settings.Current);
    }

    public string Label { get; }

    /// <summary>"Ignore modifiers" only makes sense for a hotkey that has none of its own.</summary>
    public bool CanIgnoreModifiers => !Key.HasModifiers && !AltKey.HasModifiers;

    public void Load(AppSettings current)
    {
        var hotkey = select(current);
        syncing = true;
        try
        {
            Key = HotkeyGesture.Parse(hotkey.Key);
            AltKey = HotkeyGesture.Parse(hotkey.AltKey);
            Suppress = hotkey.Suppress;
            IgnoreModifiers = hotkey.IgnoreModifiers;
        }
        finally
        {
            syncing = false;
        }
    }

    partial void OnKeyChanged(HotkeyGesture value) => Save();

    partial void OnAltKeyChanged(HotkeyGesture value) => Save();

    partial void OnSuppressChanged(bool value) => Save();

    partial void OnIgnoreModifiersChanged(bool value) => Save();

    private void Save()
    {
        if (syncing)
        {
            return;
        }

        settings.Update(s =>
        {
            var hotkey = select(s);
            hotkey.Key = Key.IsEmpty ? string.Empty : Key.ToString();
            hotkey.AltKey = AltKey.IsEmpty ? string.Empty : AltKey.ToString();
            hotkey.Suppress = Suppress;
            hotkey.IgnoreModifiers = IgnoreModifiers;
        });
    }
}
