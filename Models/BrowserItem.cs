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
