using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.Revit.UI;
using w_finder.Models;

namespace w_finder.Services;

/// <summary>
/// Builds a list of BrowserItems from Revit's PostableCommand enum AND
/// from the KeyboardShortcuts.xml file (which contains many more commands).
/// Each command becomes a searchable item that can be executed via PostCommand().
/// </summary>
public static class CommandCollector
{
    private static List<BrowserItem>? _cached;

    /// <summary>
    /// Returns all commands as BrowserItems — both PostableCommand enum values
    /// and XML-only commands from KeyboardShortcuts.xml.
    /// Requires UIApplication to look up PostableCommand IDs for XML matching.
    /// Result is cached; call Invalidate() after shortcut edits.
    /// </summary>
    public static List<BrowserItem> Collect(UIApplication uiApp)
    {
        if (_cached != null) return _cached;

        KeyboardShortcutService.Load();

        var items = new List<BrowserItem>();
        // Track which XML CommandIds we've already covered via PostableCommand
        var coveredCommandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // --- Phase 1: PostableCommand enum items ---
        foreach (PostableCommand cmd in Enum.GetValues(typeof(PostableCommand)))
        {
            string enumName = cmd.ToString();
            if (enumName == "Invalid") continue;

            // Look up the Revit internal command ID for XML matching
            string? revitCmdId = null;
            try
            {
                var cmdId = RevitCommandId.LookupPostableCommandId(cmd);
                revitCmdId = cmdId?.Name;
            }
            catch
            {
                // Some PostableCommand values may not resolve — skip silently
            }

            // Look up keyboard shortcut from XML
            string? shortcutKeys = null;
            if (revitCmdId != null)
            {
                shortcutKeys = KeyboardShortcutService.GetShortcutByCommandId(revitCmdId);
                coveredCommandIds.Add(revitCmdId);
            }

            items.Add(new BrowserItem
            {
                Name = HumanizePascalCase(enumName),
                Category = "Revit Command",
                ElementId = (long)cmd,
                Kind = BrowserItemKind.Command,
                CommandName = enumName,
                RevitCommandId = revitCmdId,
                ShortcutKeys = shortcutKeys,
                RibbonTab = "Revit Command"
            });
        }

        // --- Phase 2: XML-only commands not covered by PostableCommand ---
        long xmlId = -2000;
        foreach (var entry in KeyboardShortcutService.GetAllEntries())
        {
            if (coveredCommandIds.Contains(entry.CommandId)) continue;

            // Skip our own plugin commands
            if (entry.CommandId.Contains("Quip", StringComparison.OrdinalIgnoreCase)) continue;

            // Skip third-party add-in commands — they cannot be posted via PostCommand()
            if (entry.CommandId.StartsWith("CustomCtrl_", StringComparison.OrdinalIgnoreCase)) continue;

            var displayName = CleanXmlCommandName(entry.CommandName);
            var category = FormatPaths(entry.Paths);
            var shortcut = KeyboardShortcutService.GetShortcutByCommandId(entry.CommandId);

            items.Add(new BrowserItem
            {
                Name = displayName,
                Category = category,
                ElementId = xmlId--,
                Kind = BrowserItemKind.Command,
                CommandName = null, // not a PostableCommand
                RevitCommandId = entry.CommandId,
                ShortcutKeys = shortcut,
                RibbonTab = ExtractRibbonTab(entry.Paths)
            });
        }

        // Inject synthetic "Quip Settings" command so it appears in : command mode
        items.Add(new BrowserItem
        {
            Name = "Quip Settings",
            Category = "Plugin",
            ElementId = -9999,
            Kind = BrowserItemKind.Command,
            CommandName = "__quip_settings",
            RevitCommandId = null
        });

        _cached = items;
        return _cached;
    }

    /// <summary>
    /// Clears the cache so the next Collect() call rebuilds from scratch.
    /// Call after shortcut edits.
    /// </summary>
    public static void Invalidate()
    {
        _cached = null;
    }

    /// <summary>
    /// Converts "ProjectUnits" → "Project Units", "MEPSystemBrowser" → "MEP System Browser".
    /// </summary>
    private static string HumanizePascalCase(string input)
    {
        return Regex.Replace(input, @"(?<=[a-z])([A-Z])|(?<=[A-Z])([A-Z][a-z])", " $1$2");
    }

    /// <summary>
    /// Cleans an XML CommandName for display.
    /// "Void Forms:Void Extrusion" → "Void Extrusion" (use text after last colon)
    /// "Load Family; Load Framing Family; Load Shapes" → "Load Family" (use first semicolon segment)
    /// </summary>
    private static string CleanXmlCommandName(string raw)
    {
        // If it contains a colon, use the part after the last colon (sub-command name)
        var colonIdx = raw.LastIndexOf(':');
        if (colonIdx >= 0 && colonIdx < raw.Length - 1)
            raw = raw.Substring(colonIdx + 1).Trim();

        // If it contains semicolons, use the first segment (primary name)
        var semiIdx = raw.IndexOf(';');
        if (semiIdx >= 0)
            raw = raw.Substring(0, semiIdx).Trim();

        return raw;
    }

    /// <summary>
    /// Extracts the top-level ribbon tab from the Paths attribute.
    /// "Architecture>Build; Structure>Structure" → "Architecture"
    /// </summary>
    private static string ExtractRibbonTab(string? paths)
    {
        if (string.IsNullOrEmpty(paths)) return "Other";

        var semiIdx = paths.IndexOf(';');
        var first = semiIdx >= 0 ? paths.Substring(0, semiIdx).Trim() : paths.Trim();

        var gtIdx = first.IndexOf('>');
        return gtIdx >= 0 ? first.Substring(0, gtIdx).Trim() : first.Trim();
    }

    /// <summary>
    /// Formats the Paths attribute for display as a category.
    /// "Architecture>Build; Structure>Structure" → "Architecture > Build"
    /// Uses the first path only.
    /// </summary>
    private static string FormatPaths(string? paths)
    {
        if (string.IsNullOrEmpty(paths)) return "Revit Command";

        // Take first path (before semicolon)
        var semiIdx = paths.IndexOf(';');
        var first = semiIdx >= 0 ? paths.Substring(0, semiIdx).Trim() : paths.Trim();

        // Replace > with spaced arrow
        return first.Replace(">", " > ");
    }
}
