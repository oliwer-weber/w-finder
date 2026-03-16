using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace w_finder.Models;

public enum BrowserItemKind
{
    View,
    Sheet,
    Schedule,
    Family,
    FamilyType,
    Group,
    RevitLink,
    Assembly,
    Command,
    Shebang
}

/// <summary>
/// Represents a single item from the Revit project browser (view, sheet, schedule, family, etc.).
/// </summary>
public class BrowserItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    public required string Name
    {
        get => _name;
        init { _name = value; NameLower = value.ToLowerInvariant(); }
    }

    private string _category = string.Empty;
    public required string Category
    {
        get => _category;
        init { _category = value; CategoryLower = value.ToLowerInvariant(); }
    }

    /// <summary>Pre-computed lowercase for zero-allocation fuzzy matching.</summary>
    public string NameLower { get; private set; } = string.Empty;
    public string CategoryLower { get; private set; } = string.Empty;

    public long ElementId { get; init; }
    public BrowserItemKind Kind { get; init; }

    /// <summary>
    /// For Command items: the PostableCommand enum name (used to look up the RevitCommandId).
    /// Null for XML-only commands that aren't in the PostableCommand enum.
    /// </summary>
    public string? CommandName { get; init; }

    /// <summary>
    /// The Revit internal command ID string (e.g. "ID_EDIT_PASTE", "CustomCtrl_%...").
    /// Used for keyboard shortcut lookups and for executing XML-only commands.
    /// </summary>
    public string? RevitCommandId { get; init; }

    /// <summary>
    /// Keyboard shortcut display text (e.g. "Ctrl+S", "VG").
    /// Null if no shortcut is assigned.
    /// </summary>
    public string? ShortcutKeys { get; set; }

    /// <summary>
    /// True if at least one instance of this family type exists in the project.
    /// Set during collection, used by the "filter placed types" setting.
    /// </summary>
    public bool IsPlacedInProject { get; set; }

    /// <summary>
    /// The family name portion for FamilyType items (e.g. "Single-Flush" from "Single-Flush: 36x84").
    /// Used for two-stage family navigation grouping.
    /// </summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// The type name portion for FamilyType items (e.g. "36x84" from "Single-Flush: 36x84").
    /// </summary>
    public string? TypeName { get; init; }

    /// <summary>
    /// Number of types in this family (set on family-level summary rows).
    /// </summary>
    public int TypeCount { get; set; }

    /// <summary>Plural-aware label like "3 types" or "1 type".</summary>
    public string TypeCountLabel => TypeCount == 1 ? "1 type" : $"{TypeCount} types";

    /// <summary>
    /// True if this is a synthetic "back" row in two-stage family navigation.
    /// </summary>
    public bool IsBackRow { get; set; }

    /// <summary>
    /// Section/group key for result grouping (e.g. "VIEWS", "DOORS", "Architecture > Build").
    /// </summary>
    public string GroupKey { get; set; } = string.Empty;

    /// <summary>
    /// The ribbon tab this command belongs to (for Command mode grouping).
    /// </summary>
    public string? RibbonTab { get; init; }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set { _isFavorite = value; OnPropertyChanged(); OnPropertyChanged(nameof(FavoriteIcon)); }
    }

    /// <summary>
    /// Returns a filled or empty star for the UI.
    /// </summary>
    public string FavoriteIcon => IsFavorite ? "\u2605" : "\u2606";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
