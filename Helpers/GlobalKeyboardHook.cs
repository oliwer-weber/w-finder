using System.Diagnostics;
using System.Runtime.InteropServices;
using w_finder.Services;

namespace w_finder.Helpers;

/// <summary>
/// Low-level Windows keyboard hook that intercepts a configurable hotkey
/// before Revit can route it to schedule cells or other input-capturing views.
///
/// How it works:
/// - SetWindowsHookEx installs a callback that Windows calls on EVERY keypress system-wide.
/// - We check if the pressed key + modifiers match the configured hotkey.
/// - If they match and Revit is the foreground app, we fire the callback and suppress the key.
/// - Otherwise we pass the key along to the next hook in the chain.
///
/// The hook must be installed from a thread with a message pump (the Revit main thread has one).
/// The delegate must be stored in a static field to prevent garbage collection.
/// </summary>
public static class GlobalKeyboardHook
{
    // ── Win32 constants ──────────────────────────────────────────────
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    // Virtual key state mask: if the high bit is set, the key is currently pressed
    private const int KEY_PRESSED = 0x8000;

    // Virtual key codes for modifier keys
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LMENU = 0xA4;  // Left Alt
    private const int VK_RMENU = 0xA5;  // Right Alt

    // Our modifier flag constants (stored in settings)
    public const int MOD_CTRL = 0x02;
    public const int MOD_ALT = 0x01;
    public const int MOD_SHIFT = 0x04;

    // ── P/Invoke declarations ────────────────────────────────────────
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    // ── Hook state ───────────────────────────────────────────────────

    // CRITICAL: Store the delegate in a static field so the garbage collector
    // doesn't collect it while Windows still holds a pointer to it.
    private static LowLevelKeyboardProc? _hookProc;
    private static IntPtr _hookId = IntPtr.Zero;
    private static Action? _onHotkeyPressed;

    // Cached hotkey values (updated when settings change) so we don't
    // allocate a new QuipSettings object on every single keypress.
    private static int _hotkeyKey;
    private static int _hotkeyModifiers;

    // Our own process ID, used to check if Revit is the foreground app.
    private static readonly uint _ownProcessId = (uint)Process.GetCurrentProcess().Id;

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>
    /// Install the low-level keyboard hook. Call from a thread with a message pump
    /// (e.g. the Revit main thread during OnStartup).
    /// </summary>
    /// <param name="onHotkeyPressed">
    /// Called when the configured hotkey is pressed and Revit is the foreground app.
    /// This fires on the hook thread — use RevitBackgroundTask.Raise() inside.
    /// </param>
    public static void Install(Action onHotkeyPressed)
    {
        if (_hookId != IntPtr.Zero) return; // already installed

        _onHotkeyPressed = onHotkeyPressed;
        RefreshHotkeyCache();

        // Subscribe to settings changes so the hotkey can be reconfigured at runtime
        SettingsService.SettingsChanged += RefreshHotkeyCache;

        // Store delegate in static field to prevent GC collection
        _hookProc = HookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(
            WH_KEYBOARD_LL,
            _hookProc,
            GetModuleHandle(curModule.ModuleName),
            0); // 0 = all threads (global hook)
    }

    /// <summary>
    /// Remove the keyboard hook. Call during App.OnShutdown.
    /// </summary>
    public static void Uninstall()
    {
        SettingsService.SettingsChanged -= RefreshHotkeyCache;

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        _hookProc = null;
        _onHotkeyPressed = null;
    }

    /// <summary>
    /// Formats a hotkey (modifier flags + virtual key code) as a human-readable string.
    /// Example: "Ctrl + Space", "Alt + Shift + F1"
    /// </summary>
    public static string FormatHotkey(int modifiers, int vk)
    {
        var parts = new List<string>();
        if ((modifiers & MOD_CTRL) != 0) parts.Add("Ctrl");
        if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(KeyName(vk));
        return string.Join(" + ", parts);
    }

    // ── Private implementation ───────────────────────────────────────

    private static void RefreshHotkeyCache()
    {
        var s = SettingsService.Current;
        _hotkeyKey = s.HotkeyKey;
        _hotkeyModifiers = s.HotkeyModifiers;
    }

    /// <summary>
    /// The callback Windows calls on every keypress. Must be fast (&lt;300ms).
    /// </summary>
    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            // Read the virtual key code from the KBDLLHOOKSTRUCT (first 4 bytes of lParam)
            int vkCode = Marshal.ReadInt32(lParam);

            if (vkCode == _hotkeyKey && ModifiersMatch())
            {
                // Only fire if Revit (our process) is the foreground application
                if (IsOwnProcessForeground())
                {
                    _onHotkeyPressed?.Invoke();

                    // Return 1 to suppress the key — prevents it from reaching
                    // the schedule cell or any other Revit input handler.
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Check if the currently held modifier keys match the configured hotkey modifiers.
    /// Uses GetAsyncKeyState which returns the real-time physical key state.
    /// </summary>
    private static bool ModifiersMatch()
    {
        bool ctrlDown = (GetAsyncKeyState(VK_LCONTROL) & KEY_PRESSED) != 0
                     || (GetAsyncKeyState(VK_RCONTROL) & KEY_PRESSED) != 0;
        bool altDown = (GetAsyncKeyState(VK_LMENU) & KEY_PRESSED) != 0
                    || (GetAsyncKeyState(VK_RMENU) & KEY_PRESSED) != 0;
        bool shiftDown = (GetAsyncKeyState(VK_LSHIFT) & KEY_PRESSED) != 0
                      || (GetAsyncKeyState(VK_RSHIFT) & KEY_PRESSED) != 0;

        bool wantCtrl = (_hotkeyModifiers & MOD_CTRL) != 0;
        bool wantAlt = (_hotkeyModifiers & MOD_ALT) != 0;
        bool wantShift = (_hotkeyModifiers & MOD_SHIFT) != 0;

        // All required modifiers must be pressed, and no extra modifiers should be held
        return ctrlDown == wantCtrl && altDown == wantAlt && shiftDown == wantShift;
    }

    /// <summary>
    /// Check if the foreground window belongs to our own process (Revit).
    /// This prevents the hotkey from firing when other applications are focused.
    /// </summary>
    private static bool IsOwnProcessForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == _ownProcessId;
    }

    /// <summary>
    /// Convert a virtual key code to a readable name.
    /// </summary>
    private static string KeyName(int vk) => vk switch
    {
        0x20 => "Space",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Escape",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),           // 0-9
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),           // A-Z
        >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",            // F1-F24
        0xBE => ".",
        0xBC => ",",
        0xBD => "-",
        0xBB => "=",
        0xBA => ";",
        0xDE => "'",
        0xDB => "[",
        0xDD => "]",
        0xDC => "\\",
        0xBF => "/",
        0xC0 => "`",
        _ => $"Key(0x{vk:X2})"
    };
}
