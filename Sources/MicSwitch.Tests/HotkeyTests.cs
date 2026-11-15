using System.Windows.Input;
using MicSwitch.Hotkeys;
using MicSwitch.Services;
using MicSwitch.Settings;

namespace MicSwitch.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("MouseXButton2", 0, (int)MouseKey.XButton2, (int)HotkeyModifiers.None)]
    [InlineData("MouseMiddle", 0, (int)MouseKey.Middle, (int)HotkeyModifiers.None)]
    [InlineData("F13", 0x7C, (int)MouseKey.None, (int)HotkeyModifiers.None)]
    [InlineData("Ctrl+Alt+M", 0x4D, (int)MouseKey.None, (int)(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt))]
    [InlineData("control + shift + F1", 0x70, (int)MouseKey.None, (int)(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift))]
    [InlineData("Win+OemTilde", 0xC0, (int)MouseKey.None, (int)HotkeyModifiers.Win)]
    [InlineData("RightCtrl", 0xA3, (int)MouseKey.None, (int)HotkeyModifiers.None)]
    [InlineData("VK255", 255, (int)MouseKey.None, (int)HotkeyModifiers.None)]
    public void Parses_saved_hotkeys(string text, int virtualKey, int mouse, int modifiers) =>
        Assert.Equal(new HotkeyGesture(virtualKey, (MouseKey)mouse, (HotkeyModifiers)modifiers), HotkeyGesture.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("NotAKey")]
    [InlineData("Ctrl+")]
    [InlineData("42")]
    public void Unknown_or_empty_text_is_no_hotkey(string? text) => Assert.True(HotkeyGesture.Parse(text).IsEmpty);

    [Theory]
    [InlineData("MouseXButton2")]
    [InlineData("Ctrl+Alt+F13")]
    [InlineData("Shift+Win+A")]
    [InlineData("LeftAlt")]
    [InlineData("VK255")]
    public void Saved_text_round_trips(string text) => Assert.Equal(text, HotkeyGesture.Parse(text).ToString());

    [Fact]
    public void A_modifier_key_as_the_hotkey_does_not_also_count_as_its_own_modifier()
    {
        var gesture = HotkeyGesture.FromKey(Key.LeftCtrl, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);

        Assert.Equal(HotkeyModifiers.Shift, gesture.Modifiers);
    }

    [Theory]
    [InlineData("MouseXButton2", "Mouse 5")]
    [InlineData("MouseXButton1", "Mouse 4")]
    [InlineData("Ctrl+Alt+F13", "Ctrl + Alt + F13")]
    [InlineData("D5", "5")]
    [InlineData("Return", "Enter")]
    [InlineData("", "")]
    public void Shows_readable_names(string text, string expected) =>
        Assert.Equal(expected, HotkeyGesture.Parse(text).DisplayText);
}

public class HotkeyMatcherTests
{
    private const int F13 = 0x7C;
    private const int LeftCtrl = 0xA2;

    private readonly HotkeyMatcher matcher = new();
    private readonly List<(HotkeyAction Action, bool IsDown)> events = [];

    private void Emit(HotkeyBinding binding, bool isDown) => events.Add((binding.Action, isDown));

    private static HotkeyBinding Binding(string gesture, bool suppress = true, bool ignoreModifiers = false, HotkeyAction action = HotkeyAction.MicMain) =>
        new(action, HotkeyGesture.Parse(gesture), suppress, ignoreModifiers);

    [Fact]
    public void A_held_key_gives_one_press_and_one_release()
    {
        matcher.SetBindings([Binding("F13")]);

        Assert.True(matcher.OnKey(F13, true, HotkeyModifiers.None, Emit));
        Assert.True(matcher.OnKey(F13, true, HotkeyModifiers.None, Emit)); // auto-repeat
        Assert.True(matcher.OnKey(F13, true, HotkeyModifiers.None, Emit));
        Assert.True(matcher.OnKey(F13, false, HotkeyModifiers.None, Emit));

        Assert.Equal([(HotkeyAction.MicMain, true), (HotkeyAction.MicMain, false)], events);
    }

    [Fact]
    public void Other_keys_pass_through()
    {
        matcher.SetBindings([Binding("F13")]);

        Assert.False(matcher.OnKey(0x41, true, HotkeyModifiers.None, Emit));
        Assert.False(matcher.OnKey(0x41, false, HotkeyModifiers.None, Emit));
        Assert.False(matcher.OnKey(F13, false, HotkeyModifiers.None, Emit)); // release without a press
        Assert.Empty(events);
    }

    [Fact]
    public void Modifiers_must_match_exactly_unless_ignored()
    {
        matcher.SetBindings([Binding("Ctrl+F13")]);
        Assert.False(matcher.OnKey(F13, true, HotkeyModifiers.None, Emit));
        Assert.False(matcher.OnKey(F13, true, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, Emit));
        Assert.Empty(events);
        Assert.True(matcher.OnKey(F13, true, HotkeyModifiers.Ctrl, Emit));
        Assert.Single(events);

        events.Clear();
        matcher.SetBindings([Binding("F13", ignoreModifiers: true)]);
        Assert.True(matcher.OnKey(F13, true, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Emit));
        Assert.Single(events);
    }

    [Fact]
    public void A_release_counts_even_if_the_modifier_was_let_go_first()
    {
        matcher.SetBindings([Binding("Ctrl+F13")]);

        matcher.OnKey(F13, true, HotkeyModifiers.Ctrl, Emit);
        Assert.True(matcher.OnKey(F13, false, HotkeyModifiers.None, Emit));

        Assert.Equal([(HotkeyAction.MicMain, true), (HotkeyAction.MicMain, false)], events);
    }

    [Fact]
    public void A_modifier_key_can_be_the_hotkey()
    {
        matcher.SetBindings([Binding("LeftCtrl")]);

        // Windows reports Ctrl as held while the Ctrl key itself repeats.
        // The key is never swallowed, even with "block" on: Ctrl+C and friends must keep working.
        Assert.False(matcher.OnKey(LeftCtrl, true, HotkeyModifiers.None, Emit));
        Assert.False(matcher.OnKey(LeftCtrl, true, HotkeyModifiers.Ctrl, Emit));
        Assert.False(matcher.OnKey(LeftCtrl, false, HotkeyModifiers.Ctrl, Emit));

        Assert.Equal([(HotkeyAction.MicMain, true), (HotkeyAction.MicMain, false)], events);
    }

    [Fact]
    public void A_hotkey_that_is_not_blocked_is_passed_on()
    {
        matcher.SetBindings([Binding("MouseXButton2", suppress: false)]);

        Assert.False(matcher.OnMouse(MouseKey.XButton2, true, HotkeyModifiers.None, Emit));
        Assert.False(matcher.OnMouse(MouseKey.XButton2, false, HotkeyModifiers.None, Emit));
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Mouse_buttons_work_and_the_primary_buttons_are_never_swallowed()
    {
        matcher.SetBindings([Binding("MouseXButton2"), Binding("MouseLeft", action: HotkeyAction.MicMute)]);

        Assert.True(matcher.OnMouse(MouseKey.XButton2, true, HotkeyModifiers.None, Emit));
        Assert.False(matcher.OnMouse(MouseKey.XButton1, true, HotkeyModifiers.None, Emit));
        Assert.False(matcher.OnMouse(MouseKey.Left, true, HotkeyModifiers.None, Emit));

        Assert.Equal([(HotkeyAction.MicMain, true), (HotkeyAction.MicMute, true)], events);
    }

    [Fact]
    public void A_key_held_while_hotkeys_are_saved_again_stays_held()
    {
        matcher.SetBindings([Binding("F13")]);
        matcher.OnKey(F13, true, HotkeyModifiers.None, Emit);

        matcher.SetBindings([Binding("F13"), Binding("F14", action: HotkeyAction.MicMute)]);
        matcher.OnKey(F13, true, HotkeyModifiers.None, Emit);
        matcher.OnKey(F13, false, HotkeyModifiers.None, Emit);

        Assert.Equal([(HotkeyAction.MicMain, true), (HotkeyAction.MicMain, false)], events);
    }

    [Fact]
    public void Release_all_lets_go_of_held_keys()
    {
        matcher.SetBindings([Binding("F13")]);
        matcher.OnKey(F13, true, HotkeyModifiers.None, Emit);

        matcher.ReleaseAll(Emit);
        matcher.ReleaseAll(Emit);

        Assert.Equal([(HotkeyAction.MicMain, true), (HotkeyAction.MicMain, false)], events);
    }
}

public class HotkeyBindingTests
{
    [Fact]
    public void Only_enabled_groups_get_hotkeys()
    {
        var settings = new AppSettings();
        settings.Microphone.Hotkey = new HotkeySetting { Key = "MouseXButton2", AltKey = "F13", IgnoreModifiers = true };
        settings.Microphone.MuteHotkey = new HotkeySetting { Key = "F14" };
        settings.Speakers.VolumeUpHotkey = new HotkeySetting { Key = "Ctrl+Up", IgnoreModifiers = true };

        var bindings = HotkeyService.BuildBindings(settings).ToList();
        Assert.Equal(2, bindings.Count);
        Assert.All(bindings, x => Assert.Equal(HotkeyAction.MicMain, x.Action));
        Assert.All(bindings, x => Assert.True(x.IgnoreModifiers));

        settings.Microphone.ExtraHotkeysEnabled = true;
        settings.Speakers.ExtraHotkeysEnabled = true;
        bindings = HotkeyService.BuildBindings(settings).ToList();

        Assert.Equal(4, bindings.Count);
        Assert.Contains(bindings, x => x.Action == HotkeyAction.MicMute);

        // "Ignore modifiers" is meaningless for a hotkey that names modifiers.
        Assert.False(bindings.Single(x => x.Action == HotkeyAction.SpeakerVolumeUp).IgnoreModifiers);
    }

    [Fact]
    public void Speakers_get_a_main_hotkey_and_the_same_extra_hotkeys_as_the_microphone()
    {
        var settings = new AppSettings();
        settings.Speakers.Hotkey = new HotkeySetting { Key = "F20" };
        settings.Speakers.PushToMuteHotkey = new HotkeySetting { Key = "F21" };
        settings.Speakers.VolumeDownHotkey = new HotkeySetting { Key = "F22" };

        var bindings = HotkeyService.BuildBindings(settings).ToList();
        Assert.Equal(HotkeyAction.SpeakerMain, Assert.Single(bindings).Action);

        settings.Speakers.ExtraHotkeysEnabled = true;
        bindings = HotkeyService.BuildBindings(settings).ToList();
        Assert.Equal(
            [HotkeyAction.SpeakerMain, HotkeyAction.SpeakerPushToMute, HotkeyAction.SpeakerVolumeDown],
            bindings.Select(x => x.Action));
    }
}
