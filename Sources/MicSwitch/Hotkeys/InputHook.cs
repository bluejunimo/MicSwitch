using System.ComponentModel;
using System.Runtime.InteropServices;
using static MicSwitch.Infrastructure.NativeMethods;

namespace MicSwitch.Hotkeys;

/// <summary>
/// System-wide keyboard and mouse hooks on a thread of their own. A hook is installed only while
/// some hotkey needs it. Keys are matched and nothing else: key presses are never logged or stored.
/// </summary>
internal sealed class InputHook : IDisposable
{
    private const uint WM_APPLY = WM_APP + 1;
    private const uint WM_RELEASE_ALL = WM_APP + 2;

    private readonly HotkeyMatcher matcher;
    private readonly Action<HotkeyBinding, bool> sink;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();

    // Windows keeps raw pointers to these, so they must stay referenced for as long as the hooks exist.
    private readonly LowLevelProc keyboardProc;
    private readonly LowLevelProc mouseProc;

    private uint threadId;
    private IntPtr keyboardHook;
    private IntPtr mouseHook;
    private volatile bool wantKeyboard;
    private volatile bool wantMouse;
    private volatile bool paused;

    public InputHook(HotkeyMatcher matcher, Action<HotkeyBinding, bool> sink)
    {
        this.matcher = matcher;
        this.sink = sink;
        keyboardProc = KeyboardProc;
        mouseProc = MouseProc;
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "InputHook",
            // Every key press and mouse move in the system waits on this thread.
            Priority = ThreadPriority.Highest
        };
        thread.Start();
        ready.Wait();
    }

    /// <summary>While paused, hotkeys neither fire nor swallow input (used while the user records a new hotkey).</summary>
    public bool Paused
    {
        get => paused;
        set
        {
            paused = value;
            if (value)
            {
                PostThreadMessage(threadId, WM_RELEASE_ALL, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    public void Configure(bool keyboard, bool mouse)
    {
        wantKeyboard = keyboard;
        wantMouse = mouse;
        PostThreadMessage(threadId, WM_APPLY, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Reinstalls the hooks. Windows removes a low-level hook without notice if it ever responds too
    /// slowly (e.g. the machine was under heavy load or resuming from sleep), so this is called now and then.
    /// </summary>
    public void Reinstall() => PostThreadMessage(threadId, WM_APPLY, new IntPtr(1), IntPtr.Zero);

    public void Dispose()
    {
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // creates this thread's message queue
        ready.Set();

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            switch (message.message)
            {
                case WM_APPLY:
                    if (message.wParam != IntPtr.Zero)
                    {
                        Unhook(ref keyboardHook);
                        Unhook(ref mouseHook);
                    }

                    Apply(ref keyboardHook, wantKeyboard, WH_KEYBOARD_LL, keyboardProc);
                    Apply(ref mouseHook, wantMouse, WH_MOUSE_LL, mouseProc);
                    break;

                case WM_RELEASE_ALL:
                    matcher.ReleaseAll(sink);
                    break;
            }
        }

        Unhook(ref keyboardHook);
        Unhook(ref mouseHook);
    }

    private static void Apply(ref IntPtr hook, bool wanted, int kind, LowLevelProc proc)
    {
        if (!wanted)
        {
            Unhook(ref hook);
        }
        else if (hook == IntPtr.Zero)
        {
            hook = SetWindowsHookEx(kind, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                Log.Error(new Win32Exception(Marshal.GetLastWin32Error()), "Could not install input hook {Kind}", kind);
            }
        }
    }

    private static void Unhook(ref IntPtr hook)
    {
        if (hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == 0 && !paused)
        {
            try
            {
                var message = (int)wParam;
                var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
                if (isDown || message is WM_KEYUP or WM_SYSKEYUP)
                {
                    var virtualKey = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
                    if (matcher.OnKey(virtualKey, isDown, HeldModifiers(), sink))
                    {
                        return new IntPtr(1);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Keyboard hook failed");
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == 0 && !paused)
        {
            try
            {
                var (button, isDown) = (int)wParam switch
                {
                    WM_LBUTTONDOWN => (MouseKey.Left, true),
                    WM_LBUTTONUP => (MouseKey.Left, false),
                    WM_RBUTTONDOWN => (MouseKey.Right, true),
                    WM_RBUTTONUP => (MouseKey.Right, false),
                    WM_MBUTTONDOWN => (MouseKey.Middle, true),
                    WM_MBUTTONUP => (MouseKey.Middle, false),
                    WM_XBUTTONDOWN => (ExtraButton(lParam), true),
                    WM_XBUTTONUP => (ExtraButton(lParam), false),
                    _ => (MouseKey.None, false) // moves and wheel: by far the most common, and ignored
                };

                if (button != MouseKey.None && matcher.OnMouse(button, isDown, HeldModifiers(), sink))
                {
                    return new IntPtr(1);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Mouse hook failed");
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static MouseKey ExtraButton(IntPtr lParam)
    {
        // MSLLHOOKSTRUCT.mouseData: the high word says which extra button.
        var mouseData = Marshal.ReadInt32(lParam, 8);
        return (mouseData >> 16) switch
        {
            1 => MouseKey.XButton1,
            2 => MouseKey.XButton2,
            _ => MouseKey.None
        };
    }

    private static HotkeyModifiers HeldModifiers()
    {
        var result = HotkeyModifiers.None;
        if (IsDown(0x11))
        {
            result |= HotkeyModifiers.Ctrl;
        }

        if (IsDown(0x12))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (IsDown(0x10))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (IsDown(0x5B) || IsDown(0x5C))
        {
            result |= HotkeyModifiers.Win;
        }

        return result;
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
