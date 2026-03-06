using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace w_finder.Services;

/// <summary>
/// Persists favorite element IDs per Revit project.
/// - Local models: sidecar JSON file next to the .rvt
/// - Cloud models (ACC/BIM 360): JSON file in AppData using the model's GUID
/// </summary>
public static class FavoritesStore
{
    // Cached path so we can save from the WPF thread without needing the Document
    private static string? _currentFilePath;

    // AppData folder for cloud model favorites
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "w_finder", "favorites");

    private static string GetFilePath(Document doc)
    {
        if (doc.IsModelInCloud)
        {
            // Cloud models have a unique model GUID we can use as a filename
            var cloudPath = doc.GetCloudModelPath();
            var modelGuid = cloudPath.GetModelGUID();
            var projectGuid = cloudPath.GetProjectGUID();
            return Path.Combine(AppDataFolder, $"{projectGuid}_{modelGuid}.json");
        }

        // Local models: sidecar file next to the .rvt
        return doc.PathName + ".wfinder-favorites.json";
    }

    public static HashSet<long> Load(Document doc)
    {
        if (string.IsNullOrEmpty(doc.PathName) && !doc.IsModelInCloud)
            return new HashSet<long>();

        _currentFilePath = GetFilePath(doc);

        if (!File.Exists(_currentFilePath))
            return new HashSet<long>();

        try
        {
            string json = File.ReadAllText(_currentFilePath);
            var ids = JsonSerializer.Deserialize<List<long>>(json);
            return ids != null ? new HashSet<long>(ids) : new HashSet<long>();
        }
        catch
        {
            return new HashSet<long>();
        }
    }

    /// <summary>
    /// Saves favorites using the cached file path. Safe to call from the WPF thread.
    /// </summary>
    public static void Save(HashSet<long> favoriteIds)
    {
        if (string.IsNullOrEmpty(_currentFilePath))
            return;

        try
        {
            // Ensure the directory exists (needed for cloud model AppData path)
            var dir = Path.GetDirectoryName(_currentFilePath);
            if (dir != null)
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(favoriteIds.ToList());
            File.WriteAllText(_currentFilePath, json);
        }
        catch
        {
            // Silently fail if the file is locked or path is invalid
        }
    }
}
