using System.IO;
using System.Xml.Linq;

namespace w_finder.Services;

/// <summary>
/// Parses, caches, and modifies Revit's KeyboardShortcuts.xml.
/// Shortcuts are user-configurable and stored per Revit version.
/// </summary>
public static class KeyboardShortcutService
{
    private static List<ShortcutEntry>? _entries;
    private static Dictionary<string, ShortcutEntry>? _byCommandId;
    private static string? _xmlPath;

    public class ShortcutEntry
    {
        public required string CommandName { get; init; }
        public required string CommandId { get; init; }
        public string? Shortcuts { get; set; }
        public string? Paths { get; init; }
    }

    /// <summary>
    /// Returns the path to the KeyboardShortcuts.xml file for Revit 2025.
    /// </summary>
    private static string GetXmlPath()
    {
        if (_xmlPath == null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _xmlPath = Path.Combine(appData, "Autodesk", "Revit", "Autodesk Revit 2025", "KeyboardShortcuts.xml");
        }
        return _xmlPath;
    }

    /// <summary>
    /// Loads and parses the XML. Called lazily on first access.
    /// </summary>
    public static void Load()
    {
        if (_entries != null) return;

        _entries = new List<ShortcutEntry>();
        _byCommandId = new Dictionary<string, ShortcutEntry>(StringComparer.OrdinalIgnoreCase);

        var path = GetXmlPath();
        if (!File.Exists(path)) return;

        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Root;
            if (root == null) return;

            foreach (var el in root.Elements("ShortcutItem"))
            {
                var commandName = el.Attribute("CommandName")?.Value;
                var commandId = el.Attribute("CommandId")?.Value;
                if (commandName == null || commandId == null) continue;

                var entry = new ShortcutEntry
                {
                    CommandName = commandName,
                    CommandId = commandId,
                    Shortcuts = el.Attribute("Shortcuts")?.Value,
                    Paths = el.Attribute("Paths")?.Value
                };

                _entries.Add(entry);
                // Use first entry per CommandId (duplicates shouldn't occur but be safe)
                _byCommandId.TryAdd(commandId, entry);
            }
        }
        catch
        {
            // If the file is corrupted or unreadable, proceed with empty data
        }
    }

    /// <summary>
    /// Returns the first shortcut string for a given CommandId, or null.
    /// Multi-shortcuts are separated by '#' in the XML; we return the first one.
    /// </summary>
    public static string? GetShortcutByCommandId(string commandId)
    {
        Load();
        if (_byCommandId == null || !_byCommandId.TryGetValue(commandId, out var entry))
            return null;
        return FirstShortcut(entry.Shortcuts);
    }

    /// <summary>
    /// Returns all parsed shortcut entries.
    /// </summary>
    public static IReadOnlyList<ShortcutEntry> GetAllEntries()
    {
        Load();
        return _entries ?? new List<ShortcutEntry>();
    }

    /// <summary>
    /// Assigns a shortcut to a command in the XML file.
    /// Appends to existing shortcuts with '#' separator.
    /// Returns true on success.
    /// </summary>
    public static bool AssignShortcut(string commandId, string shortcutKeys)
    {
        Load();
        var path = GetXmlPath();
        if (!File.Exists(path)) return false;

        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Root;
            if (root == null) return false;

            foreach (var el in root.Elements("ShortcutItem"))
            {
                if (string.Equals(el.Attribute("CommandId")?.Value, commandId, StringComparison.OrdinalIgnoreCase))
                {
                    var existing = el.Attribute("Shortcuts")?.Value;
                    var newValue = string.IsNullOrEmpty(existing) ? shortcutKeys : existing + "#" + shortcutKeys;
                    el.SetAttributeValue("Shortcuts", newValue);
                    doc.Save(path);

                    // Update cache
                    if (_byCommandId != null && _byCommandId.TryGetValue(commandId, out var entry))
                        entry.Shortcuts = newValue;

                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Removes all shortcuts from a command in the XML file.
    /// Returns true on success.
    /// </summary>
    public static bool RemoveShortcut(string commandId)
    {
        Load();
        var path = GetXmlPath();
        if (!File.Exists(path)) return false;

        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Root;
            if (root == null) return false;

            foreach (var el in root.Elements("ShortcutItem"))
            {
                if (string.Equals(el.Attribute("CommandId")?.Value, commandId, StringComparison.OrdinalIgnoreCase))
                {
                    el.Attribute("Shortcuts")?.Remove();
                    doc.Save(path);

                    // Update cache
                    if (_byCommandId != null && _byCommandId.TryGetValue(commandId, out var entry))
                        entry.Shortcuts = null;

                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clears the cache so the next access re-reads the XML.
    /// </summary>
    public static void Invalidate()
    {
        _entries = null;
        _byCommandId = null;
    }

    /// <summary>
    /// Returns the first shortcut from a '#'-separated list, or null.
    /// </summary>
    private static string? FirstShortcut(string? shortcuts)
    {
        if (string.IsNullOrEmpty(shortcuts)) return null;
        var idx = shortcuts.IndexOf('#');
        return idx >= 0 ? shortcuts.Substring(0, idx) : shortcuts;
    }
}
