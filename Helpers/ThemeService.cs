using System.IO;

namespace w_finder.Helpers;

/// <summary>
/// Manages the light/dark theme preference. Persists the choice
/// in a small file under AppData so it survives Revit restarts.
/// </summary>
public static class ThemeService
{
    private static bool _isDark;

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Rauncher", "theme.txt");

    static ThemeService()
    {
        // Load saved preference on first access
        try
        {
            if (File.Exists(SettingsFile))
                _isDark = File.ReadAllText(SettingsFile).Trim() == "dark";
        }
        catch
        {
            _isDark = false;
        }
    }

    public static bool IsDarkMode() => _isDark;

    public static void SetDarkMode(bool isDark)
    {
        _isDark = isDark;
        try
        {
            var dir = Path.GetDirectoryName(SettingsFile);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsFile, isDark ? "dark" : "light");
        }
        catch
        {
            // Silently fail
        }
    }
}
