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
    Command
}

/// <summary>
/// Represents a single item from the Revit project browser (view, sheet, schedule, family, etc.).
/// </summary>
public class BrowserItem : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public long ElementId { get; init; }
    public BrowserItemKind Kind { get; init; }

    /// <summary>
    /// For Command items: the PostableCommand enum name (used to look up the RevitCommandId).
    /// </summary>
    public string? CommandName { get; init; }

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
