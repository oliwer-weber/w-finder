using System.IO;
using System.Text.Json;

namespace w_finder.Services;

/// <summary>
/// Central settings persistence for Quip.
/// Stores all preferences in a single JSON file under AppData.
/// Replaces the old ThemeService (migrates theme.txt automatically).
/// </summary>
public static class SettingsService
{
    private static QuipSettings _settings = new();

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Quip");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    // Legacy theme file — migrated on first load, then ignored
    private static readonly string LegacyThemeFile = Path.Combine(SettingsDir, "theme.txt");

    /// <summary>
    /// Fired after Save() so subscribers (e.g. the pane) can react to changes.
    /// </summary>
    public static event Action? SettingsChanged;

    static SettingsService()
    {
        Load();
    }

    /// <summary>
    /// Returns a copy of the current settings so callers can mutate freely.
    /// </summary>
    public static QuipSettings Current => new()
    {
        IsDarkMode = _settings.IsDarkMode,
        DefaultExportPath = _settings.DefaultExportPath,
        FilterPlacedTypesOnly = _settings.FilterPlacedTypesOnly,
        HotkeyKey = _settings.HotkeyKey,
        HotkeyModifiers = _settings.HotkeyModifiers,
        LaunchBehavior = _settings.LaunchBehavior,
        DefaultMode = _settings.DefaultMode,
        LastActiveMode = _settings.LastActiveMode,
        LastSearchText = _settings.LastSearchText
    };

    /// <summary>
    /// Persists updated settings and notifies subscribers.
    /// </summary>
    public static void Save(QuipSettings settings)
    {
        _settings = settings;
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Silently fail — same pattern as old ThemeService
        }
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// Saves the current mode and search text so they can be restored on next launch.
    /// Does NOT fire SettingsChanged — this is a silent background save.
    /// </summary>
    public static void SaveLastState(int mode, string searchText)
    {
        _settings.LastActiveMode = mode;
        _settings.LastSearchText = searchText;
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }

    private static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                _settings = JsonSerializer.Deserialize<QuipSettings>(json) ?? new();
                return;
            }

            // Migrate from legacy theme.txt if it exists
            if (File.Exists(LegacyThemeFile))
            {
                var themeText = File.ReadAllText(LegacyThemeFile).Trim();
                _settings = new QuipSettings
                {
                    IsDarkMode = themeText == "dark"
                };
                // Save in new format so migration only happens once
                Save(_settings);
            }
        }
        catch
        {
            _settings = new();
        }
    }
}

public enum LaunchBehavior { CleanSlate = 0, RememberMode = 1, RememberAll = 2 }
public enum DefaultLaunchMode { Browser = 0, Place = 1, Command = 2, Shebang = 3 }

/// <summary>
/// All Quip user preferences. Add new settings here as needed.
/// </summary>
public class QuipSettings
{
    public bool IsDarkMode { get; set; }
    public string DefaultExportPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public bool FilterPlacedTypesOnly { get; set; }

    /// <summary>Virtual key code for the global hotkey. Default: 0x20 = Space.</summary>
    public int HotkeyKey { get; set; } = 0x20;

    /// <summary>Modifier flags for the global hotkey. Default: 0x02 = Ctrl. (Ctrl=0x02, Alt=0x01, Shift=0x04)</summary>
    public int HotkeyModifiers { get; set; } = 0x02;

    /// <summary>What to restore when the pane opens. 0=CleanSlate, 1=RememberMode, 2=RememberAll.</summary>
    public int LaunchBehavior { get; set; }

    /// <summary>Default mode when LaunchBehavior is CleanSlate. 0=Browser, 1=Place, 2=Command, 3=Shebang.</summary>
    public int DefaultMode { get; set; }

    /// <summary>Last active mode (saved on pane hide for restore).</summary>
    public int LastActiveMode { get; set; }

    /// <summary>Last search text (saved on pane hide for restore).</summary>
    public string LastSearchText { get; set; } = string.Empty;
}
