using System.Runtime.InteropServices;

namespace w_finder.Services;

/// <summary>
/// Simulates a Revit keyboard shortcut (e.g. "ZE", "SSS", "Ctrl+S", "Shift+F1")
/// using the Win32 keybd_event API. Used to fire commands that aren't accessible
/// via UIApplication.PostCommand and don't expose a clickable ribbon handler — if
/// the user has assigned a shortcut to such a command in Revit, sending the keys
/// routes through Revit's own dispatcher, the exact same path a real keypress takes.
///
/// Caller is responsible for hiding our dockable pane before calling — keystrokes
/// land in whatever window has focus, so the pane must release focus first.
/// </summary>
public static class KeystrokeSimulator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_MENU = 0x12; // Alt

    /// <summary>
    /// Parses the shortcut and, if it's something we can send, presses the keys.
    /// Returns true on success. The diagnostic string is a one-token reason for
    /// the outcome (used by CommandDiagnostics logging).
    /// </summary>
    public static bool TrySend(string shortcut, IntPtr revitHwnd, out string diagnostic)
    {
        diagnostic = "init";

        if (string.IsNullOrWhiteSpace(shortcut) || shortcut == "-")
        {
            diagnostic = "empty";
            return false;
        }

        // Parse first so we can bail without side effects on unsupported formats.
        if (!TryParse(shortcut, out var modifiers, out var mainKey))
        {
            diagnostic = $"unparseable:{shortcut}";
            return false;
        }

        // Bring Revit's main window to the foreground so keystrokes land there.
        if (revitHwnd != IntPtr.Zero)
        {
            SetForegroundWindow(revitHwnd);
        }
        // Tiny pause so the focus change actually settles before we start typing.
        Thread.Sleep(50);

        try
        {
            // Press modifiers (if any)
            foreach (var m in modifiers) keybd_event(m, 0, 0, IntPtr.Zero);

            // Send each key in the main sequence
            foreach (var vk in mainKey)
            {
                keybd_event(vk, 0, 0, IntPtr.Zero);
                keybd_event(vk, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
                Thread.Sleep(15); // small gap so Revit doesn't merge keypresses
            }

            // Release modifiers in reverse order
            for (int i = modifiers.Count - 1; i >= 0; i--)
                keybd_event(modifiers[i], 0, KEYEVENTF_KEYUP, IntPtr.Zero);

            diagnostic = modifiers.Count > 0 ? "sent-combo" : "sent-seq";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"ex-{ex.GetType().Name}";
            return false;
        }
    }

    /// <summary>
    /// Parses "ZE" / "SSS" (letter sequence) or "Ctrl+S" / "Shift+F1" (modifier
    /// combo) into a list of modifier VKs and a list of main-key VKs.
    /// Returns false if any token is unsupported.
    /// </summary>
    private static bool TryParse(string shortcut, out List<byte> modifiers, out List<byte> mainKey)
    {
        modifiers = new List<byte>();
        mainKey = new List<byte>();

        if (shortcut.Contains('+'))
        {
            var parts = shortcut.Split('+');
            // Everything but the last token is a modifier
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToUpperInvariant())
                {
                    case "CTRL": case "CONTROL": modifiers.Add(VK_CONTROL); break;
                    case "SHIFT":                 modifiers.Add(VK_SHIFT); break;
                    case "ALT": case "MENU":      modifiers.Add(VK_MENU); break;
                    default: return false;
                }
            }

            // Last token is the actual key — single char or function key
            var key = parts[^1].Trim();
            if (!TryParseKey(key, out var vk)) return false;
            mainKey.Add(vk);
            return true;
        }
        else
        {
            // Sequence form: "ZE", "SSS", "VG". Each char is one key press.
            foreach (char c in shortcut)
            {
                if (!TryParseKey(c.ToString(), out var vk)) return false;
                mainKey.Add(vk);
            }
            return mainKey.Count > 0;
        }
    }

    /// <summary>
    /// Types a plain-text string (letters/digits/spaces) into whatever window
    /// currently has focus. Used to pre-fill Revit's Keyboard Shortcuts dialog
    /// search box with a command name. Skips characters we can't map to a VK
    /// (punctuation, symbols) — fine for Revit command names which are
    /// effectively alphabetic.
    ///
    /// Does NOT call SetForegroundWindow — caller should not steal focus from
    /// whatever dialog Revit just opened.
    /// </summary>
    public static void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (char c in text)
        {
            byte vk;
            if (c == ' ') vk = 0x20; // VK_SPACE
            else if (c >= 'A' && c <= 'Z') vk = (byte)c;
            else if (c >= 'a' && c <= 'z') vk = (byte)(c - 32);
            else if (c >= '0' && c <= '9') vk = (byte)c;
            else continue;

            keybd_event(vk, 0, 0, IntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            Thread.Sleep(15);
        }
    }

    private static bool TryParseKey(string key, out byte vk)
    {
        vk = 0;
        if (key.Length == 1)
        {
            char c = key[0];
            if (c >= 'a' && c <= 'z') { vk = (byte)(c - 32); return true; }
            if (c >= 'A' && c <= 'Z') { vk = (byte)c; return true; }
            if (c >= '0' && c <= '9') { vk = (byte)c; return true; }
            return false;
        }

        // Function keys F1–F12 (VK 0x70–0x7B)
        if (key.Length >= 2 && (key[0] == 'F' || key[0] == 'f') &&
            int.TryParse(key.AsSpan(1), out int n) && n >= 1 && n <= 12)
        {
            vk = (byte)(0x70 + n - 1);
            return true;
        }

        return false;
    }
}
