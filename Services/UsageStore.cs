using System.IO;
using System.Text.Json;
using w_finder.Models;

namespace w_finder.Services;

/// <summary>
/// Tracks how often and how recently each item is used, for usage-based ranking
/// in the unified search surface. Stores two scopes:
///   - per-project (views/sheets/families/groups/links/etc) keyed by ElementId
///   - app-global (Revit commands, shebangs) keyed by stable string ID
///
/// Ranking uses frecency: useCount × exp(-deltaDays · ln2 / HalfLifeDays).
/// </summary>
public static class UsageStore
{
    private const double HalfLifeDays = 30.0;
    private const int MaxEntriesPerScope = 500;

    private static readonly string StoreDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Quip", "usage");

    private static readonly string GlobalFile = Path.Combine(StoreDir, "global.json");

    private static string? _projectFile;
    private static UsageData _projectData = new();
    private static UsageData _globalData = new();
    private static bool _globalLoaded;

    /// <summary>
    /// Sets the active project. Loads its per-project usage file (creating an empty
    /// one if missing) and lazy-loads the app-global file if not already loaded.
    /// </summary>
    public static void SetProject(string projectKey)
    {
        _projectFile = Path.Combine(StoreDir, $"proj_{SanitizeKey(projectKey)}.json");
        _projectData = LoadFile(_projectFile);

        if (!_globalLoaded)
        {
            _globalData = LoadFile(GlobalFile);
            _globalLoaded = true;
        }
    }

    /// <summary>
    /// Records a usage event for the given item. Increments its use count and
    /// stamps LastUsed = now. New entries are inserted with UseCount = 1.
    /// </summary>
    public static void RecordUsage(BrowserItem item)
    {
        bool global = IsGlobalKind(item.Kind);
        var data = global ? _globalData : _projectData;

        UsageEntry? entry = null;
        if (global)
        {
            // For commands/shebangs, prefer CommandName as a stable cross-version key.
            // Fall back to ElementId if CommandName is null (shouldn't happen for these kinds).
            string key = item.CommandName ?? item.ElementId.ToString();
            entry = data.Entries.Find(e => e.StringId == key);
            if (entry == null)
            {
                entry = new UsageEntry { StringId = key, ElementId = item.ElementId };
                data.Entries.Add(entry);
            }
        }
        else
        {
            entry = data.Entries.Find(e => e.ElementId == item.ElementId);
            if (entry == null)
            {
                entry = new UsageEntry { ElementId = item.ElementId };
                data.Entries.Add(entry);
            }
        }

        entry.DisplayName = item.Name;
        entry.Kind = (int)item.Kind;
        entry.UseCount++;
        entry.LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        TrimIfOversized(data);
        SaveFile(global ? GlobalFile : _projectFile, data);
    }

    /// <summary>
    /// Returns the frecency score for the given item, or 0 if not tracked.
    /// </summary>
    public static double GetScore(BrowserItem item)
    {
        UsageEntry? entry = LookupEntry(item);
        return entry == null ? 0.0 : ComputeScore(entry);
    }

    /// <summary>
    /// Returns the top-ranked entries (highest frecency first), optionally
    /// filtered by Kind. Merges per-project and app-global stores.
    /// </summary>
    public static List<UsageEntry> GetTopRanked(int count, BrowserItemKind? kindFilter = null)
    {
        var all = _projectData.Entries.Concat(_globalData.Entries);
        if (kindFilter.HasValue)
            all = all.Where(e => e.Kind == (int)kindFilter.Value);

        return all
            .Select(e => (entry: e, score: ComputeScore(e)))
            .Where(t => t.score > 0)
            .OrderByDescending(t => t.score)
            .Take(count)
            .Select(t => t.entry)
            .ToList();
    }

    /// <summary>
    /// Returns the most-recently-used entries (LastUsed desc), optionally
    /// filtered by Kind. Used by the legacy recents-style empty state.
    /// </summary>
    public static List<UsageEntry> GetRecents(int count, BrowserItemKind? kindFilter = null)
    {
        var all = _projectData.Entries.Concat(_globalData.Entries);
        if (kindFilter.HasValue)
            all = all.Where(e => e.Kind == (int)kindFilter.Value);

        return all
            .OrderByDescending(e => e.LastUsed)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// True for kinds that should be tracked app-globally (stable IDs across projects).
    /// </summary>
    public static bool IsGlobalKind(BrowserItemKind kind)
        => kind == BrowserItemKind.Command || kind == BrowserItemKind.Shebang;

    private static UsageEntry? LookupEntry(BrowserItem item)
    {
        if (IsGlobalKind(item.Kind))
        {
            string? key = item.CommandName;
            if (key == null) return null;
            return _globalData.Entries.Find(e => e.StringId == key);
        }
        return _projectData.Entries.Find(e => e.ElementId == item.ElementId);
    }

    private static double ComputeScore(UsageEntry entry)
    {
        if (entry.UseCount <= 0) return 0.0;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        double deltaDays = Math.Max(0, (now - entry.LastUsed) / 86400.0);
        double decay = Math.Exp(-deltaDays * Math.Log(2) / HalfLifeDays);
        return entry.UseCount * decay;
    }

    private static void TrimIfOversized(UsageData data)
    {
        if (data.Entries.Count <= MaxEntriesPerScope) return;

        // Drop the lowest-frecency entries until under cap.
        data.Entries.Sort((a, b) => ComputeScore(b).CompareTo(ComputeScore(a)));
        data.Entries.RemoveRange(MaxEntriesPerScope, data.Entries.Count - MaxEntriesPerScope);
    }

    private static UsageData LoadFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new UsageData();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UsageData>(json) ?? new UsageData();
        }
        catch
        {
            return new UsageData();
        }
    }

    private static void SaveFile(string? path, UsageData data)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Directory.CreateDirectory(StoreDir);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Silently fail — same pattern as SettingsService.
        }
    }

    private static string SanitizeKey(string key)
        => key.GetHashCode().ToString("x8");
}

public class UsageData
{
    public List<UsageEntry> Entries { get; set; } = new();
}

public class UsageEntry
{
    /// <summary>Revit ElementId (per-project scope). 0 when StringId is used.</summary>
    public long ElementId { get; set; }

    /// <summary>Stable string key (app-global scope, e.g. Revit PostableCommand name, shebang id).</summary>
    public string? StringId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>BrowserItemKind cast to int (avoid enum-versioning hazards in JSON).</summary>
    public int Kind { get; set; }

    public int UseCount { get; set; }

    /// <summary>Unix seconds.</summary>
    public long LastUsed { get; set; }
}
