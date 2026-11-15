using System.Windows;
using System.Windows.Input;
using MicSwitch.Hotkeys;

namespace MicSwitch.UI.Controls;

/// <summary>
/// A box that records a hotkey. Click it, then press a key combination or a mouse button.
/// Backspace, Delete or Escape clears it. Left and right click need a double-click, so that a single
/// click can still put the cursor in the box.
/// </summary>
internal sealed class HotkeyBox : Wpf.Ui.Controls.TextBox
{
    public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
        nameof(Gesture),
        typeof(HotkeyGesture),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata(HotkeyGesture.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).ShowGesture()));

    private Key pendingModifier = Key.None;
    private long focusedAt;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        ContextMenu = null;
        ClearButtonEnabled = false;
        PlaceholderText = "None";
        Cursor = Cursors.Hand;
        InputMethod.SetIsInputMethodEnabled(this, false);
        ToolTip = "Click, then press a key or mouse button. Backspace clears.";
    }

    /// <summary>Raised with true when any box starts recording and false when it stops.</summary>
    public static event Action<bool>? RecordingChanged;

    public HotkeyGesture Gesture
    {
        get => (HotkeyGesture)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        focusedAt = Environment.TickCount64;
        PlaceholderText = "Press a key…";
        RecordingChanged?.Invoke(true);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        pendingModifier = Key.None;
        PlaceholderText = "None";
        RecordingChanged?.Invoke(false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };

        if (key == Key.Tab)
        {
            return; // keep keyboard navigation working
        }

        e.Handled = true;
        var modifiers = HeldModifiers();
        if (IsModifier(key))
        {
            // A modifier on its own becomes the hotkey only if it is released without another key.
            pendingModifier = key;
            return;
        }

        pendingModifier = Key.None;
        Gesture = key is Key.Back or Key.Delete or Key.Escape && modifiers == HotkeyModifiers.None
            ? HotkeyGesture.None
            : HotkeyGesture.FromKey(key, modifiers);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.None && key == pendingModifier)
        {
            Gesture = HotkeyGesture.FromKey(key);
            e.Handled = true;
        }

        pendingModifier = Key.None;
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (!IsKeyboardFocusWithin)
        {
            return; // this click only gives the box focus
        }

        // A double-click that started as the click into the box doesn't count.
        var settled = Environment.TickCount64 - focusedAt > 600;
        var button = e.ChangedButton switch
        {
            MouseButton.Middle => MouseKey.Middle,
            MouseButton.XButton1 => MouseKey.XButton1,
            MouseButton.XButton2 => MouseKey.XButton2,
            MouseButton.Left when e.ClickCount == 2 && settled => MouseKey.Left,
            MouseButton.Right when e.ClickCount == 2 && settled => MouseKey.Right,
            _ => MouseKey.None
        };

        if (button != MouseKey.None)
        {
            pendingModifier = Key.None;
            Gesture = HotkeyGesture.FromMouse(button, HeldModifiers());
            e.Handled = true;
        }
    }

    private void ShowGesture() => Text = Gesture.DisplayText;

    private static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static HotkeyModifiers HeldModifiers()
    {
        var result = HotkeyModifiers.None;
        var held = Keyboard.Modifiers;
        if (held.HasFlag(ModifierKeys.Control))
        {
            result |= HotkeyModifiers.Ctrl;
        }

        if (held.HasFlag(ModifierKeys.Alt))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (held.HasFlag(ModifierKeys.Shift))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (held.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            result |= HotkeyModifiers.Win;
        }

        return result;
    }
}
