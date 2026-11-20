using w_finder.Helpers;
using w_finder.Models;
using w_finder.Services;

namespace w_finder.Views;

// !ut (whole project) and !us (families of the current selection): find
// element types with zero instances and delete the ones you tick.
//
// Flow: scan > pick categories > confirm > delete. Selection is held on the
// type IDs rather than on the rows, so a category row and the type rows
// underneath it are always two views of the same truth.
public partial class FinderPaneView
{
    private const string UnusedSelectAllKey = "unused-select-all";
    private const string UnusedDeleteSelectedKey = "unused-delete-selected";
    private const string UnusedDeleteConfirmedKey = "unused-delete-confirmed";
    private const string UnusedRescanKey = "unused-rescan";

    /// <summary>The most recent unused-type scan, kept for the delete actions.</summary>
    private UnusedTypeScan? _unusedScan;

    /// <summary>Element IDs of the types currently ticked for deletion.</summary>
    private readonly HashSet<long> _unusedSelected = new();

    /// <summary>
    /// Null for !ut (whole project). For !us, the selection's sibling families,
    /// captured once at launch so the rescan after a delete stays on them.
    /// </summary>
    private UnusedTypeScope? _unusedScope;

    /// <summary>Types staged by a confirm page, waiting on the user's Enter.</summary>
    private List<UnusedType> _pendingUnusedTypes = new();

    /// <summary>Breadcrumb prefix, so a scoped report never looks like a project-wide one.</summary>
    private string UnusedCrumb => _unusedScope == null ? "TYPES" : "TYPES IN SELECTION";

    /// <summary>
    /// !ut: scans model categories for element types with zero instances and
    /// opens the category picker in the pane.
    /// </summary>
    private void LaunchUnusedTypes()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                ShowError("Open a project first");
                return;
            }

            var scan = UnusedTypeService.Scan(doc);

            Dispatcher.Invoke(() => OpenUnusedReport(scan, null));
        });
    }

    /// <summary>
    /// !us: same report as !ut, but only for the other types of the families
    /// the selected elements belong to (all "Basic Wall" types for a basic
    /// wall, every type of the door family for a door, and so on).
    /// </summary>
    private void LaunchUnusedTypesOfSelection()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null)
            {
                ShowError("Open a project first");
                return;
            }
            var doc = uidoc.Document;

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds.Count == 0)
            {
                ShowError("Select elements in the model first");
                return;
            }

            var scope = UnusedTypeScope.FromSelection(doc, selIds);
            if (scope.IsEmpty)
            {
                ShowError("Nothing in the selection has a type");
                return;
            }

            var scan = UnusedTypeService.Scan(doc, scope);

            Dispatcher.Invoke(() => OpenUnusedReport(scan, scope));
        });
    }

    /// <summary>Shared by !ut and !us: resets the ticks and opens the category page.</summary>
    private void OpenUnusedReport(UnusedTypeScan scan, UnusedTypeScope? scope)
    {
        _unusedScope = scope;
        _unusedScan = scan;
        _unusedSelected.Clear();
        App.ViewModel.EnterReportMode(BuildUnusedCategoryPage(scan), "TYPES", DrillUnusedTypes);
    }

    /// <summary>Runs the !ut / !us action rows.</summary>
    private void HandleUnusedTypesAction(string? actionKey)
    {
        switch (actionKey)
        {
            case UnusedSelectAllKey:
                ToggleAllUnusedCategories();
                break;

            case UnusedDeleteSelectedKey:
                StageUnusedDeletion(SelectedUnusedTypes(), "Categories");
                break;

            case UnusedDeleteConfirmedKey:
                RunUnusedDeletion();
                break;

            case UnusedRescanKey:
                if (_unusedScan != null)
                    App.ViewModel.ReplaceReportPage(BuildUnusedCategoryPage(_unusedScan));
                break;
        }
    }

    /// <summary>The unused types in one category, in scan order.</summary>
    private List<UnusedType> TypesInCategory(string category) =>
        _unusedScan == null
            ? new List<UnusedType>()
            : _unusedScan.Unused.Where(t => t.CategoryName == category).ToList();

    /// <summary>Tri-state for a category: all ticked, none ticked, or some.</summary>
    private bool? CategoryCheckState(string category)
    {
        var types = TypesInCategory(category);
        if (types.Count == 0) return false;

        var ticked = types.Count(t => _unusedSelected.Contains(t.ElementId));
        if (ticked == 0) return false;
        if (ticked == types.Count) return true;
        return null;
    }

    /// <summary>
    /// Space on a category row: tick the whole category, unless it is already
    /// fully ticked, in which case clear it.
    /// </summary>
    private void ToggleCategory(string category)
    {
        var types = TypesInCategory(category);
        var turnOn = CategoryCheckState(category) != true;

        foreach (var t in types)
        {
            if (turnOn) _unusedSelected.Add(t.ElementId);
            else _unusedSelected.Remove(t.ElementId);
        }
    }

    /// <summary>The "Select all categories" row, and Ctrl+A / Ctrl+D.</summary>
    private void SetAllUnusedCategories(bool select)
    {
        _unusedSelected.Clear();
        if (select && _unusedScan != null)
        {
            foreach (var t in _unusedScan.Unused)
                _unusedSelected.Add(t.ElementId);
        }
        App.ViewModel.RefreshCurrentReportPage();
    }

    /// <summary>Flips between everything selected and nothing selected.</summary>
    private void ToggleAllUnusedCategories()
    {
        var everything = _unusedScan?.Unused.Count ?? 0;
        SetAllUnusedCategories(_unusedSelected.Count < everything);
    }

    /// <summary>
    /// The root page: one row per category with a tri-state tick, a select-all
    /// row, and the delete row. Space ticks a whole category, Enter opens it.
    /// </summary>
    private ReportPage BuildUnusedCategoryPage(UnusedTypeScan scan)
    {
        var page = new ReportPage
        {
            SetAllChecks = SetAllUnusedCategories,
        };

        if (scan.Unused.Count == 0)
        {
            page.Breadcrumb = $"{UnusedCrumb} · 0 unused of {scan.TotalTypes}";
            page.Rows.Add(new ReportRow
            {
                Text = "Nothing to purge",
                Label = _unusedScope == null
                    ? $"All {scan.TotalTypes} model types have at least one instance"
                    : $"All {scan.TotalTypes} types in the selection's families are in use",
                GroupKey = "UNUSED TYPES",
            });
            return page;
        }

        page.PrimaryActionKey = UnusedDeleteSelectedKey;
        page.PrimaryActionLabel = "delete ticked";

        page.Rows.Add(new ReportRow
        {
            Text = "Select all categories",
            Label = "Toggles every category at once",
            Value = "Enter",
            ActionKey = UnusedSelectAllKey,
            GroupKey = "ACTIONS",
        });

        var deleteRow = new ReportRow
        {
            Text = "Delete selected types",
            Label = "Nothing ticked yet",
            ActionKey = UnusedDeleteSelectedKey,
            GroupKey = "ACTIONS",
        };
        page.Rows.Add(deleteRow);

        var categories = scan.Unused
            .GroupBy(t => t.CategoryName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in categories)
        {
            var category = group.Key;
            var row = new ReportRow
            {
                Text = category,
                IsCheckable = true,
                DrillKey = "unused-cat:" + category,
                GroupKey = "CATEGORIES",
                OnToggle = _ => ToggleCategory(category),
            };
            page.Rows.Add(row);

            // Start on the first category, not on the action rows above it.
            page.InitialCursorRow ??= row;
        }

        page.Recalculate = p =>
        {
            var total = _unusedScan?.Unused.Count ?? 0;
            var ticked = _unusedSelected.Count;

            p.Breadcrumb = $"{UnusedCrumb} · {total} unused of {scan.TotalTypes} · {ticked} selected";

            deleteRow.Text = ticked == 0
                ? "Delete selected types"
                : $"Delete {ticked} selected type" + (ticked == 1 ? "" : "s");
            deleteRow.Label = ticked == 0
                ? "Nothing ticked yet. Space ticks the highlighted category"
                : "Asks for confirmation first";
            deleteRow.Value = ticked == 0 ? null : "Ctrl+Enter";

            // Category rows: refresh tick state and the "n of m" counts, which
            // both change when the user edits a selection one level down.
            foreach (var row in p.Rows.Where(r => r.IsCheckable))
            {
                var types = TypesInCategory(row.Text);
                var selected = types.Count(t => _unusedSelected.Contains(t.ElementId));

                row.IsChecked = CategoryCheckState(row.Text);
                row.Value = $"{selected} of {types.Count}";
                row.Label = types.Count == 1 ? "1 unused type" : $"{types.Count} unused types";
            }
        };

        return page;
    }

    /// <summary>Enter on a category row: the types inside it, individually tickable.</summary>
    private ReportPage? DrillUnusedTypes(ReportRow row)
    {
        if (row.DrillKey == null || !row.DrillKey.StartsWith("unused-cat:")) return null;

        var category = row.DrillKey.Substring("unused-cat:".Length);
        var types = TypesInCategory(category);
        if (types.Count == 0) return null;

        var page = new ReportPage
        {
            BackLabel = "Categories",
            PrimaryActionKey = UnusedDeleteSelectedKey,
            PrimaryActionLabel = "delete ticked",
            SetAllChecks = select =>
            {
                foreach (var t in types)
                {
                    if (select) _unusedSelected.Add(t.ElementId);
                    else _unusedSelected.Remove(t.ElementId);
                }
            },
        };

        // The same delete action as on the category page, so Ctrl+Enter works
        // from inside a category too.
        var deleteRow = new ReportRow
        {
            Text = "Delete selected types",
            ActionKey = UnusedDeleteSelectedKey,
            GroupKey = "ACTIONS",
        };
        page.Rows.Add(deleteRow);

        foreach (var t in types)
        {
            var id = t.ElementId;
            var typeRow = new ReportRow
            {
                Text = t.DisplayName,
                Value = id.ToString(),
                IsCheckable = true,
                GroupKey = category.ToUpperInvariant(),
                OnToggle = _ =>
                {
                    if (!_unusedSelected.Remove(id)) _unusedSelected.Add(id);
                },
            };
            page.Rows.Add(typeRow);
            page.InitialCursorRow ??= typeRow;
        }

        page.Recalculate = p =>
        {
            var selected = types.Count(t => _unusedSelected.Contains(t.ElementId));
            var allTicked = _unusedSelected.Count;

            p.Breadcrumb = $"{UnusedCrumb} · {category} · {selected} of {types.Count} selected";

            deleteRow.Text = allTicked == 0
                ? "Delete selected types"
                : $"Delete {allTicked} selected type" + (allTicked == 1 ? "" : "s");
            deleteRow.Label = allTicked == 0
                ? "Nothing ticked yet. Space ticks the highlighted type"
                : "All categories together · asks for confirmation first";
            deleteRow.Value = allTicked == 0 ? null : "Ctrl+Enter";

            foreach (var r in p.Rows.Where(r => r.IsCheckable))
            {
                if (long.TryParse(r.Value, out var id))
                    r.IsChecked = _unusedSelected.Contains(id);
            }
        };

        return page;
    }

    /// <summary>The ticked types, in scan order.</summary>
    private List<UnusedType> SelectedUnusedTypes() =>
        _unusedScan == null
            ? new List<UnusedType>()
            : _unusedScan.Unused.Where(t => _unusedSelected.Contains(t.ElementId)).ToList();

    /// <summary>The yes/no page shown before any type is actually deleted.</summary>
    private ReportPage BuildUnusedConfirmPage(List<UnusedType> targets, string backLabel)
    {
        var page = new ReportPage
        {
            Breadcrumb = $"{UnusedCrumb} · confirm deleting {targets.Count}",
            BackLabel = backLabel,
            PrimaryActionKey = UnusedDeleteConfirmedKey,
            PrimaryActionLabel = "delete",
        };

        var categoryCount = targets.Select(t => t.CategoryName).Distinct().Count();

        page.Rows.Add(new ReportRow
        {
            Text = $"Yes, delete {targets.Count} type" + (targets.Count == 1 ? "" : "s"),
            Label = $"From {categoryCount} categor" + (categoryCount == 1 ? "y" : "ies")
                + " · a family's last type takes the family with it · Ctrl+Z still undoes this",
            Value = "Enter",
            ActionKey = UnusedDeleteConfirmedKey,
            GroupKey = "CONFIRM",
        });

        foreach (var t in targets)
        {
            page.Rows.Add(new ReportRow
            {
                Text = t.DisplayName,
                Value = t.ElementId.ToString(),
                GroupKey = t.CategoryName.ToUpperInvariant(),
            });
        }

        return page;
    }

    private void StageUnusedDeletion(List<UnusedType> targets, string backLabel)
    {
        if (targets.Count == 0)
        {
            App.ViewModel.ShowInlineError("Nothing ticked. Space ticks the highlighted row");
            return;
        }

        _pendingUnusedTypes = targets;
        App.ViewModel.PushReportPage(BuildUnusedConfirmPage(targets, backLabel));
    }

    /// <summary>Runs the staged deletion and replaces the page with the result.</summary>
    private void RunUnusedDeletion()
    {
        var ids = _pendingUnusedTypes.Select(t => t.ElementId).ToList();
        var attempted = ids.Count;
        if (attempted == 0) return;

        // Captured here, on the UI thread, so the rescan uses the scope the
        // report was opened with (the live selection may have changed since).
        var scope = _unusedScope;

        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var (deleted, failed) = UnusedTypeService.Delete(doc, ids);

            // Re-scan so the numbers shown afterwards are the real ones: a
            // family deleted with its last type takes its siblings along.
            var rescan = UnusedTypeService.Scan(doc, scope);

            Dispatcher.Invoke(() =>
            {
                _pendingUnusedTypes.Clear();
                _unusedSelected.Clear();
                _unusedScan = rescan;

                var page = new ReportPage
                {
                    Breadcrumb = $"{UnusedCrumb} · deleted {deleted} of {attempted}",
                };

                page.Rows.Add(new ReportRow
                {
                    Text = $"Deleted {deleted} of {attempted} type" + (attempted == 1 ? "" : "s"),
                    Label = failed.Count == 0
                        ? scope == null
                            ? $"{rescan.Unused.Count} unused type(s) left in the project"
                            : $"{rescan.Unused.Count} unused type(s) left in the selection's families"
                        : $"{failed.Count} could not be deleted",
                    GroupKey = "RESULT",
                });

                var backRow = new ReportRow
                {
                    Text = "Back to categories",
                    Label = "Re-scanned after the delete",
                    Value = "Enter",
                    ActionKey = UnusedRescanKey,
                    GroupKey = "RESULT",
                };
                page.Rows.Add(backRow);
                page.InitialCursorRow = backRow; // so Enter carries on straight away

                foreach (var f in failed)
                    page.Rows.Add(new ReportRow { Text = f, GroupKey = "FAILED" });

                // Replace rather than push: the pages behind this one describe a
                // model that no longer exists.
                App.ViewModel.ClearReportStack();
                App.ViewModel.ReplaceReportPage(page);
            });
        });
    }
}
