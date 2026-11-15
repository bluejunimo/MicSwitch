namespace MicSwitch.Hotkeys;

internal enum HotkeyAction
{
    MicMain,
    MicToggle,
    MicMute,
    MicUnmute,
    MicPushToTalk,
    MicPushToMute,
    SpeakerToggle,
    SpeakerMute,
    SpeakerUnmute,
    SpeakerVolumeUp,
    SpeakerVolumeDown
}

internal sealed record HotkeyBinding(HotkeyAction Action, HotkeyGesture Gesture, bool Suppress, bool IgnoreModifiers);

/// <summary>
/// Turns raw key and mouse-button events into hotkey press/release events. It runs inside the
/// low-level input hook, where Windows gives a callback only a few hundred milliseconds before it
/// drops the hook, so it does no allocation, locking or I/O.
/// </summary>
internal sealed class HotkeyMatcher
{
    private sealed class Entry(HotkeyBinding binding)
    {
        public readonly HotkeyBinding Binding = binding;
        public bool Pressed;
    }

    private volatile Entry[] entries = [];

    public bool HasKeyboardBindings => entries.Any(x => !x.Binding.Gesture.IsMouse);

    public bool HasMouseBindings => entries.Any(x => x.Binding.Gesture.IsMouse);

    public void SetBindings(IEnumerable<HotkeyBinding> bindings)
    {
        var old = entries;
        entries = bindings
            .Where(x => !x.Gesture.IsEmpty)
            .Select(binding => new Entry(binding)
            {
                // A key that is held down while settings are saved stays held.
                Pressed = old.Any(x => x.Pressed && x.Binding == binding)
            })
            .ToArray();
    }

    /// <summary>Returns true when the key event should be hidden from other applications.</summary>
    public bool OnKey(int virtualKey, bool isDown, HotkeyModifiers heldModifiers, Action<HotkeyBinding, bool> emit)
    {
        var ownModifier = HotkeyGesture.ModifierOf(virtualKey);
        var suppress = Process(virtualKey, MouseKey.None, isDown, heldModifiers & ~ownModifier, emit);

        // Swallowing Ctrl, Alt, Shift or Win would break every shortcut that uses them.
        return suppress && ownModifier == HotkeyModifiers.None;
    }

    /// <summary>Returns true when the button event should be hidden from other applications.</summary>
    public bool OnMouse(MouseKey button, bool isDown, HotkeyModifiers heldModifiers, Action<HotkeyBinding, bool> emit)
    {
        var suppress = Process(0, button, isDown, heldModifiers, emit);

        // Swallowing the primary buttons would leave the user unable to click anything.
        return suppress && button is not (MouseKey.Left or MouseKey.Right);
    }

    /// <summary>Releases everything that is held, e.g. because input is about to be ignored for a while.</summary>
    public void ReleaseAll(Action<HotkeyBinding, bool> emit)
    {
        foreach (var entry in entries)
        {
            if (entry.Pressed)
            {
                entry.Pressed = false;
                emit(entry.Binding, false);
            }
        }
    }

    private bool Process(int virtualKey, MouseKey button, bool isDown, HotkeyModifiers heldModifiers, Action<HotkeyBinding, bool> emit)
    {
        var suppress = false;
        var current = entries;
        for (var i = 0; i < current.Length; i++)
        {
            var entry = current[i];
            var gesture = entry.Binding.Gesture;
            if (gesture.VirtualKey != virtualKey || gesture.Mouse != button)
            {
                continue;
            }

            if (isDown)
            {
                if (entry.Pressed)
                {
                    // Keyboard auto-repeat.
                    suppress |= entry.Binding.Suppress;
                    continue;
                }

                if (!entry.Binding.IgnoreModifiers && heldModifiers != gesture.Modifiers)
                {
                    continue;
                }

                entry.Pressed = true;
                suppress |= entry.Binding.Suppress;
                emit(entry.Binding, true);
            }
            else if (entry.Pressed)
            {
                // Released even if the modifiers were let go first.
                entry.Pressed = false;
                suppress |= entry.Binding.Suppress;
                emit(entry.Binding, false);
            }
        }

        return suppress;
    }
}
