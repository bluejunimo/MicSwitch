using System.Windows.Input;
using MicSwitch.Infrastructure;

namespace MicSwitch.Hotkeys;

internal enum MouseKey
{
    None,
    Left,
    Right,
    Middle,
    XButton1,
    XButton2
}

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

/// <summary>
/// A key or mouse button plus modifiers. Saved as text such as "Ctrl+Alt+F13" or "MouseXButton2",
/// the same format MicSwitch 1.x used.
/// </summary>
internal readonly record struct HotkeyGesture(int VirtualKey, MouseKey Mouse, HotkeyModifiers Modifiers)
{
    private const string MousePrefix = "Mouse";
    private const string RawKeyPrefix = "VK";

    public static HotkeyGesture None => default;

    public static HotkeyGesture FromKey(Key key, HotkeyModifiers modifiers = HotkeyModifiers.None)
    {
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        return virtualKey == 0 ? None : new HotkeyGesture(virtualKey, MouseKey.None, modifiers & ~ModifierOf(virtualKey));
    }

    public static HotkeyGesture FromMouse(MouseKey button, HotkeyModifiers modifiers = HotkeyModifiers.None) =>
        button == MouseKey.None ? None : new HotkeyGesture(0, button, modifiers);

    public bool IsEmpty => VirtualKey == 0 && Mouse == MouseKey.None;

    public bool IsMouse => Mouse != MouseKey.None;

    public bool HasModifiers => Modifiers != HotkeyModifiers.None;

    /// <summary>The modifier a virtual key itself stands for, e.g. Ctrl for the left Ctrl key.</summary>
    public static HotkeyModifiers ModifierOf(int virtualKey) => virtualKey switch
    {
        0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Ctrl,
        0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
        0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
        0x5B or 0x5C => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None
    };

    public static HotkeyGesture Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return None;
        }

        var modifiers = HotkeyModifiers.None;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isLast = i == parts.Length - 1;
            if (!isLast && ParseModifier(part) is { } modifier)
            {
                modifiers |= modifier;
                continue;
            }

            if (!isLast)
            {
                return None;
            }

            if (part.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<MouseKey>(part[MousePrefix.Length..], true, out var button))
            {
                return FromMouse(button, modifiers);
            }

            if (part.StartsWith(RawKeyPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(part[RawKeyPrefix.Length..], out var raw) && raw is > 0 and < 256)
            {
                return new HotkeyGesture(raw, MouseKey.None, modifiers & ~ModifierOf(raw));
            }

            if (Enum.TryParse<Key>(part, true, out var key) && !int.TryParse(part, out _))
            {
                return FromKey(key, modifiers);
            }
        }

        return None;
    }

    /// <summary>The saved form. Round-trips through <see cref="Parse"/>.</summary>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return "None";
        }

        string main;
        if (IsMouse)
        {
            main = MousePrefix + Mouse;
        }
        else
        {
            var key = KeyInterop.KeyFromVirtualKey(VirtualKey);
            main = key == Key.None ? RawKeyPrefix + VirtualKey : key.ToString();
        }

        return string.Concat(ModifierNames().Select(x => x + "+")) + main;
    }

    /// <summary>What the user sees, e.g. "Ctrl + F13" or "Mouse 5".</summary>
    public string DisplayText => IsEmpty ? string.Empty : string.Join(" + ", ModifierNames().Append(MainDisplayName()));

    private IEnumerable<string> ModifierNames()
    {
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            yield return "Ctrl";
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            yield return "Alt";
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            yield return "Shift";
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            yield return "Win";
        }
    }

    private string MainDisplayName()
    {
        switch (Mouse)
        {
            case MouseKey.Left: return "Left click";
            case MouseKey.Right: return "Right click";
            case MouseKey.Middle: return "Middle click";
            case MouseKey.XButton1: return "Mouse 4";
            case MouseKey.XButton2: return "Mouse 5";
        }

        var key = KeyInterop.KeyFromVirtualKey(VirtualKey);
        switch (key)
        {
            case Key.None: return "Key " + VirtualKey;
            case >= Key.D0 and <= Key.D9: return ((char)('0' + (key - Key.D0))).ToString();
            case >= Key.NumPad0 and <= Key.NumPad9: return "Num " + (key - Key.NumPad0);
            case Key.Return: return "Enter";
            case Key.Back: return "Backspace";
            case Key.Capital: return "Caps Lock";
            case Key.Prior: return "Page Up";
            case Key.Next: return "Page Down";
            case Key.Snapshot: return "Print Screen";
            case Key.Scroll: return "Scroll Lock";
            case Key.LeftCtrl: return "Left Ctrl";
            case Key.RightCtrl: return "Right Ctrl";
            case Key.LeftAlt: return "Left Alt";
            case Key.RightAlt: return "Right Alt";
            case Key.LeftShift: return "Left Shift";
            case Key.RightShift: return "Right Shift";
            case Key.LWin: return "Left Win";
            case Key.RWin: return "Right Win";
            case Key.Multiply: return "Num *";
            case Key.Add: return "Num +";
            case Key.Subtract: return "Num -";
            case Key.Divide: return "Num /";
            case Key.Decimal: return "Num .";
        }

        // Punctuation keys are named after US layout positions ("OemTilde"); show what the key types instead.
        var name = key.ToString();
        if (name.StartsWith("Oem", StringComparison.Ordinal) && NativeMethods.VirtualKeyToChar(VirtualKey) is { } symbol)
        {
            return symbol.ToString();
        }

        return name;
    }

    private static HotkeyModifiers? ParseModifier(string text) => text.ToLowerInvariant() switch
    {
        "ctrl" or "control" => HotkeyModifiers.Ctrl,
        "alt" => HotkeyModifiers.Alt,
        "shift" => HotkeyModifiers.Shift,
        "win" or "windows" => HotkeyModifiers.Win,
        _ => null
    };
}
