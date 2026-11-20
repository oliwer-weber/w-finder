using Autodesk.Revit.DB;
using w_finder.Helpers;
using w_finder.Models;
using w_finder.Services;

namespace w_finder.Views;

// The report quips !dwg and !lp, plus the plumbing every report shares:
// what happens when Enter lands on an action row or a "select in model" row.
//
// A report is just pages of rows (ReportPage / ReportRow) shown in the results
// list. The view model owns navigation; this file builds the pages and runs
// the actions that need the Revit API.
public partial class FinderPaneView
{
    private const string PurgeLinePatternsKey = "purge-line-patterns";

    /// <summary>Line patterns staged for deletion by the !lp confirm page.</summary>
    private List<(string Name, long Id)> _pendingLinePatterns = new();

    /// <summary>
    /// Every action row in every report ends up here. The row's ActionKey
    /// says which quip it belongs to.
    /// </summary>
    private void OnReportActionRequested(ReportRow row)
    {
        switch (row.ActionKey)
        {
            case FilterApplyKey:
                ApplySelectionFilter();
                return;

            case PurgeLinePatternsKey:
                DeleteStagedLinePatterns();
                return;

            default:
                HandleUnusedTypesAction(row.ActionKey);
                return;
        }
    }

    /// <summary>
    /// "Select in model" rows: select the element, switch to a view that
    /// shows it, and get out of the way.
    /// </summary>
    private void OnReportElementActivated(long elementId)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            var id = new ElementId(elementId);
            if (uidoc.Document.GetElement(id) == null)
            {
                ShowError("That element no longer exists in the project");
                return;
            }

            var ids = new List<ElementId> { id };
            uidoc.Selection.SetElementIds(ids);

            // ShowElements switches to a view the element is visible in, which
            // for a view-specific import is the drafting view that hides it.
            try { uidoc.ShowElements(ids); }
            catch { /* Revit already told the user it couldn't find a good view */ }

            FinderCommand.ClosePane(uiApp);
        });
    }

    // ── !dwg ───────────────────────────────────────────────────────

    /// <summary>
    /// !dwg: collects every linked and imported CAD in the project and opens
    /// the report inside the pane.
    /// </summary>
    private void LaunchCadReport()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                ShowError("Open a project first");
                return;
            }

            var report = DwgCleanupService.CollectCad(doc);
            var pages = new CadReportPages(report);
            var root = pages.BuildRoot();

            Dispatcher.Invoke(() => App.ViewModel.EnterReportMode(root, "CAD", pages.Drill));
        });
    }

    // ── !lp ────────────────────────────────────────────────────────

    /// <summary>
    /// !lp: lists the "IMPORT-" line patterns and asks for confirmation in the
    /// pane before deleting anything.
    /// </summary>
    private void LaunchLinePatternCleanup()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                ShowError("Open a project first");
                return;
            }

            var patterns = DwgCleanupService.FindImportedLinePatterns(doc);

            Dispatcher.Invoke(() =>
            {
                _pendingLinePatterns = patterns;
                App.ViewModel.EnterReportMode(BuildLinePatternPage(patterns), "PATTERNS");
            });
        });
    }

    /// <summary>The !lp confirm page: the delete row on top, the victims below.</summary>
    private static ReportPage BuildLinePatternPage(List<(string Name, long Id)> patterns)
    {
        if (patterns.Count == 0)
        {
            return new ReportPage
            {
                Breadcrumb = "PATTERNS · nothing to clean",
                Rows =
                {
                    new ReportRow
                    {
                        Text = "No “IMPORT-” line patterns found",
                        Label = "Nothing to clean up",
                        GroupKey = "IMPORTED LINE PATTERNS",
                    }
                }
            };
        }

        var page = new ReportPage
        {
            Breadcrumb = $"PATTERNS · {patterns.Count} “IMPORT-” pattern"
                + (patterns.Count == 1 ? "" : "s"),
            PrimaryActionKey = PurgeLinePatternsKey,
            PrimaryActionLabel = "delete",
        };

        page.Rows.Add(new ReportRow
        {
            Text = $"Delete {patterns.Count} line pattern" + (patterns.Count == 1 ? "" : "s"),
            Label = "Affected lines fall back to solid · line styles are not touched",
            Value = "Enter",
            ActionKey = PurgeLinePatternsKey,
            GroupKey = "CONFIRM",
        });

        foreach (var (name, id) in patterns)
        {
            page.Rows.Add(new ReportRow
            {
                Text = name,
                Value = id.ToString(),
                GroupKey = "WILL BE DELETED",
            });
        }

        return page;
    }

    private void DeleteStagedLinePatterns()
    {
        var ids = _pendingLinePatterns.Select(p => p.Id).ToList();
        var attempted = ids.Count;
        if (attempted == 0) return;

        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var (deleted, failed) = DwgCleanupService.DeleteLinePatterns(doc, ids);

            Dispatcher.Invoke(() =>
            {
                _pendingLinePatterns.Clear();

                var page = new ReportPage
                {
                    Breadcrumb = $"PATTERNS · deleted {deleted} of {attempted}",
                };

                page.Rows.Add(new ReportRow
                {
                    Text = $"Deleted {deleted} of {attempted} line pattern"
                        + (attempted == 1 ? "" : "s"),
                    Label = failed.Count == 0
                        ? "Clean. Line styles untouched"
                        : $"{failed.Count} could not be deleted",
                    GroupKey = "RESULT",
                });

                foreach (var f in failed)
                    page.Rows.Add(new ReportRow { Text = f, GroupKey = "FAILED" });

                App.ViewModel.ReplaceReportPage(page);
            });
        });
    }
}
