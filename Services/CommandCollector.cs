using System.Text.RegularExpressions;
using Autodesk.Revit.UI;
using w_finder.Models;

namespace w_finder.Services;

/// <summary>
/// Builds a list of BrowserItems from Revit's PostableCommand enum.
/// Each command becomes a searchable item that can be executed via PostCommand().
/// </summary>
public static class CommandCollector
{
    /// <summary>
    /// Returns all PostableCommand values as BrowserItems with human-readable names.
    /// Called once when the pane opens; the list is static across the session.
    /// </summary>
    public static List<BrowserItem> Collect()
    {
        var items = new List<BrowserItem>();

        foreach (PostableCommand cmd in Enum.GetValues(typeof(PostableCommand)))
        {
            // Skip the "Invalid" placeholder if it exists
            string enumName = cmd.ToString();
            if (enumName == "Invalid") continue;

            items.Add(new BrowserItem
            {
                Name = HumanizePascalCase(enumName),
                Category = "Revit Command",
                ElementId = (long)cmd,
                Kind = BrowserItemKind.Command,
                CommandName = enumName
            });
        }

        return items;
    }

    /// <summary>
    /// Converts "ProjectUnits" → "Project Units", "MEPSystemBrowser" → "MEP System Browser", etc.
    /// Inserts spaces before uppercase letters that follow a lowercase letter or before
    /// a new uppercase word in an acronym run.
    /// </summary>
    private static string HumanizePascalCase(string input)
    {
        // Insert space before: uppercase preceded by lowercase, or uppercase followed by lowercase preceded by uppercase
        return Regex.Replace(input, @"(?<=[a-z])([A-Z])|(?<=[A-Z])([A-Z][a-z])", " $1$2");
    }
}
