namespace w_finder.Models;

/// <summary>
/// Represents a single parameter row in the Property Inspector (!pi).
/// Contains both display data and metadata for editing/write-back.
/// </summary>
public class InspectorParameterItem
{
    /// <summary>Parameter display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Formatted display value (from AsValueString or equivalent).</summary>
    public string DisplayValue { get; set; } = string.Empty;

    /// <summary>Revit parameter group label (e.g. "Constraints", "Dimensions").</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>Sort order for group — matches Revit's native Properties palette ordering.</summary>
    public int GroupSortOrder { get; set; }

    /// <summary>Sort order within the group (preserves Revit's parameter ordering).</summary>
    public int SortOrder { get; set; }

    /// <summary>True if this parameter cannot be edited.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>True for instance parameters, false for type parameters.</summary>
    public bool IsInstance { get; set; }

    /// <summary>
    /// Revit StorageType as int for safe cross-thread transfer.
    /// 0=None, 1=Integer, 2=Double, 3=String, 4=ElementId
    /// </summary>
    public int StorageType { get; set; }

    /// <summary>
    /// The parameter's Definition name used to look it up on elements during write-back.
    /// </summary>
    public string DefinitionName { get; set; } = string.Empty;

    /// <summary>
    /// True if this is a Yes/No parameter (StorageType.Integer with YesNo spec).
    /// </summary>
    public bool IsYesNo { get; set; }

    /// <summary>
    /// True when multiple elements are selected and this parameter has different values.
    /// </summary>
    public bool IsVaries { get; set; }

    /// <summary>
    /// How many of the selected elements have this parameter.
    /// </summary>
    public int ApplicableElementCount { get; set; } = 1;

    /// <summary>
    /// Total number of selected elements (for "Updated N elements" status).
    /// </summary>
    public int TotalSelectedCount { get; set; } = 1;

    /// <summary>
    /// The element IDs that share this parameter (for multi-element write-back).
    /// </summary>
    public List<long> ElementIds { get; set; } = new();

    /// <summary>
    /// True if this is a type parameter (written to the type element, not instance).
    /// </summary>
    public bool IsTypeParameter => !IsInstance;

    /// <summary>
    /// True if this is a section divider row (e.g. "── Type Parameters ──").
    /// </summary>
    public bool IsDivider { get; set; }

    /// <summary>
    /// True if this is a category navigation row (mixed-category multi-select).
    /// </summary>
    public bool IsCategoryRow { get; set; }

    /// <summary>
    /// Element count for category rows.
    /// </summary>
    public int CategoryElementCount { get; set; }

    /// <summary>
    /// The category name for category rows, used for drill-in.
    /// </summary>
    public string? CategoryName { get; set; }
}
