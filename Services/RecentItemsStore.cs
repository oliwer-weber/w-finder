using System.IO;
using System.Text.Json;

namespace w_finder.Services;

/// <summary>
/// Tracks recently opened/used items per mode per project.
/// Persisted as a JSON file in AppData, keyed by project path hash.
/// </summary>
public static class RecentItemsStore
{
    private static readonly string StoreDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Quip", "recents");

    private static string? _currentFilePath;
    private static RecentData _data = new();

    private const int MaxRecentsPerMode = 10;

    public static void SetProject(string projectKey)
    {
        _currentFilePath = Path.Combine(StoreDir, $"{SanitizeKey(projectKey)}.json");
        Load();
    }

    /// <summary>
    /// Records an item as recently used in the given mode.
    /// </summary>
    public static void RecordUsage(string mode, long elementId, string displayName)
    {
        if (!_data.Recents.TryGetValue(mode, out var list))
        {
            list = new List<RecentEntry>();
            _data.Recents[mode] = list;
        }

        // Remove existing entry for this element (we'll re-add at top)
        list.RemoveAll(e => e.ElementId == elementId);

        list.Insert(0, new RecentEntry
        {
            ElementId = elementId,
            DisplayName = displayName,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        // Trim to max
        if (list.Count > MaxRecentsPerMode)
            list.RemoveRange(MaxRecentsPerMode, list.Count - MaxRecentsPerMode);

        Save();
    }

    /// <summary>
    /// Returns the most recent items for a given mode, up to count.
    /// </summary>
    public static List<RecentEntry> GetRecents(string mode, int count = 5)
    {
        if (_data.Recents.TryGetValue(mode, out var list))
            return list.Take(count).ToList();
        return new List<RecentEntry>();
    }

    private static void Load()
    {
        _data = new RecentData();
        if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
            return;

        try
        {
            var json = File.ReadAllText(_currentFilePath);
            _data = JsonSerializer.Deserialize<RecentData>(json) ?? new();
        }
        catch
        {
            _data = new RecentData();
        }
    }

    private static void Save()
    {
        if (string.IsNullOrEmpty(_currentFilePath)) return;

        try
        {
            Directory.CreateDirectory(StoreDir);
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_currentFilePath, json);
        }
        catch
        {
            // Silently fail
        }
    }

    private static string SanitizeKey(string key)
    {
        // Use a hash of the project path for the filename
        var hash = key.GetHashCode().ToString("x8");
        return $"recents_{hash}";
    }
}

public class RecentData
{
    public Dictionary<string, List<RecentEntry>> Recents { get; set; } = new();
}

public class RecentEntry
{
    public long ElementId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}
