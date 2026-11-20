namespace w_finder.Models;

/// <summary>
/// One row inside an in-pane report. A row can be inert (just information),
/// drillable (Enter opens a child page), or an action (Enter fires
/// <see cref="ViewModels.FinderPaneViewModel.ReportActionRequested"/>).
/// </summary>
public class ReportRow
{
    /// <summary>The main line of the row.</summary>
    public string Text { get; set; } = "";

    /// <summary>Small muted caption shown above <see cref="Text"/>. Null for a plain row.</summary>
    public string? Label { get; set; }

    /// <summary>Right-aligned trailing text (count, unit, status).</summary>
    public string? Value { get; set; }

    /// <summary>Section header this row sits under. Empty means no grouping.</summary>
    public string GroupKey { get; set; } = "";

    /// <summary>Non-null when Enter should open a child page. Passed to the drill provider.</summary>
    public string? DrillKey { get; set; }

    /// <summary>Non-null when Enter should run an action. Passed to the action event.</summary>
    public string? ActionKey { get; set; }

    /// <summary>Revit element to select when Enter is pressed. 0 = nothing to select.</summary>
    public long RevitElementId { get; set; }

    /// <summary>True when Space toggles a checkbox on this row.</summary>
    public bool IsCheckable { get; set; }

    /// <summary>
    /// Checkbox state: true, false, or null for partially ticked (a category
    /// where only some of its types are selected).
    /// </summary>
    public bool? IsChecked { get; set; } = false;

    /// <summary>
    /// What Space does on this row. When null the row just flips its own
    /// <see cref="IsChecked"/>; a summary row uses this to drive the real
    /// selection it stands for.
    /// </summary>
    public Action<ReportRow>? OnToggle { get; set; }

    /// <summary>True if Enter does something on this row.</summary>
    public bool IsActionable => DrillKey != null || ActionKey != null || RevitElementId != 0;
}

/// <summary>
/// One screen of an in-pane report. Pages are pushed on a stack so Escape /
/// the back row can walk out the way the user walked in.
/// </summary>
public class ReportPage
{
    /// <summary>Shown in the status line, e.g. "CAD · Imported DWGs".</summary>
    public string Breadcrumb { get; set; } = "";

    /// <summary>
    /// ActionKey of the row that Ctrl+Enter runs on this page ("apply",
    /// "delete selected"...). Null when the page has nothing to finish.
    /// </summary>
    public string? PrimaryActionKey { get; set; }

    /// <summary>
    /// Row the cursor starts on when the page first opens. Null means the
    /// first row. Lets a page keep its action rows on top without making the
    /// user arrow past them every time.
    /// </summary>
    public ReportRow? InitialCursorRow { get; set; }

    /// <summary>Short verb for the footer hint, e.g. "apply" or "delete".</summary>
    public string PrimaryActionLabel { get; set; } = "run";

    /// <summary>Text for the synthetic back row. Null on the root page.</summary>
    public string? BackLabel { get; set; }

    /// <summary>
    /// Called before the page is rendered, so a page whose rows depend on
    /// checkbox state (a live "delete N selected" row) can update itself.
    /// </summary>
    public Action<ReportPage>? Recalculate { get; set; }

    /// <summary>
    /// What Ctrl+A / Ctrl+D do on this page. When null, every checkable row is
    /// simply set to the given state.
    /// </summary>
    public Action<bool>? SetAllChecks { get; set; }

    public List<ReportRow> Rows { get; set; } = new();

    /// <summary>The checked rows, in page order.</summary>
    public List<ReportRow> CheckedRows => Rows.Where(r => r.IsCheckable && r.IsChecked == true).ToList();
}
