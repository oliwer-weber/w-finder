namespace w_finder.Models;

public enum QuickActionKind
{
    Rename,
    Delete,
    Duplicate,
    DuplicateWithDetailing,
    DuplicateDependent,
    ExcelExport,
    AssignShortcut,
    RemoveShortcut,
    EditFamily
}

public class QuickAction
{
    public required QuickActionKind Kind { get; init; }
    public required string Label { get; init; }
    public required string Shortcut { get; init; }
}
