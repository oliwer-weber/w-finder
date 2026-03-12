using w_finder.Models;

namespace w_finder.Services;

/// <summary>
/// Determines which quick actions are available for a given BrowserItem.
/// </summary>
public static class QuickActionResolver
{
    private static readonly QuickAction Rename = new() { Kind = QuickActionKind.Rename, Label = "Rename", Shortcut = "F2" };
    private static readonly QuickAction Delete = new() { Kind = QuickActionKind.Delete, Label = "Delete", Shortcut = "Del" };
    private static readonly QuickAction Duplicate = new() { Kind = QuickActionKind.Duplicate, Label = "Duplicate", Shortcut = "D" };
    private static readonly QuickAction DupDetailing = new() { Kind = QuickActionKind.DuplicateWithDetailing, Label = "Dup+Detail", Shortcut = "Shift+D" };
    private static readonly QuickAction DupDependent = new() { Kind = QuickActionKind.DuplicateDependent, Label = "Dependent", Shortcut = "Ctrl+D" };
    private static readonly QuickAction SetShortcut = new() { Kind = QuickActionKind.AssignShortcut, Label = "Set Shortcut", Shortcut = "S" };
    private static readonly QuickAction ClearShortcut = new() { Kind = QuickActionKind.RemoveShortcut, Label = "Remove Shortcut", Shortcut = "R" };

    public static List<QuickAction> Resolve(BrowserItem item)
    {
        return item.Kind switch
        {
            BrowserItemKind.View => new List<QuickAction> { Rename, Delete, Duplicate, DupDetailing, DupDependent },
            BrowserItemKind.Sheet => new List<QuickAction> { Rename, Delete, Duplicate, DupDetailing },
            BrowserItemKind.Schedule => new List<QuickAction> { Rename, Delete, Duplicate },
            BrowserItemKind.Family or BrowserItemKind.FamilyType => new List<QuickAction> { Rename, Delete },
            BrowserItemKind.Group => new List<QuickAction> { Rename, Delete },
            BrowserItemKind.RevitLink or BrowserItemKind.Assembly => new List<QuickAction> { Rename, Delete },
            BrowserItemKind.Command => BuildCommandActions(item),
            _ => new List<QuickAction>(),
        };
    }

    private static List<QuickAction> BuildCommandActions(BrowserItem item)
    {
        var actions = new List<QuickAction> { SetShortcut };
        if (!string.IsNullOrEmpty(item.ShortcutKeys))
            actions.Add(ClearShortcut);
        return actions;
    }
}
