namespace w_finder.Models;

public enum QuickActionKind
{
    Rename,
    Delete,
    Duplicate,
    DuplicateWithDetailing,
    DuplicateDependent
}

public class QuickAction
{
    public required QuickActionKind Kind { get; init; }
    public required string Label { get; init; }
    public required string Shortcut { get; init; }
}
