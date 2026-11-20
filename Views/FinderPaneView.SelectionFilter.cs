using Autodesk.Revit.DB;
using w_finder.Helpers;
using w_finder.Models;

namespace w_finder.Views;

// !sf, Selection Filter: narrow the current Revit selection by ticking
// categories, families, types or single instances.
//
// It is drawn with the same report engine as !ut and !dwg (pages of rows with
// tick boxes), so it gets the shared keys for free: Space ticks, Enter / Right
// opens a level, Left / Esc goes back, Ctrl+Enter applies.
public partial class FinderPaneView
{
    private const string FilterApplyKey = "sf-apply";

    /// <summary>The Category > Family > Type > Instance tree of the launch selection.</summary>
    private List<SelectionFilterNode> _filterRoots = new();

    /// <summary>Lets a drill row find its tree node again. Key = the row's DrillKey.</summary>
    private readonly Dictionary<string, SelectionFilterNode> _filterDrillNodes = new();

    /// <summary>
    /// Reads the current Revit selection, builds the tree, and opens the filter.
    /// </summary>
    private void LaunchSelectionFilter()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null)
            {
                ShowError("Open a project first");
                return;
            }

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds.Count == 0)
            {
                ShowError("Select elements in the model first");
                return;
            }

            var doc = uidoc.Document;
            var elements = new List<ElementData>();

            foreach (var id in selIds)
            {
                var elem = doc.GetElement(id);
                if (elem == null) continue;

                string catName = elem.Category?.Name ?? "Uncategorized";
                string famName;
                string typeName;

                if (elem is FamilyInstance fi)
                {
                    famName = fi.Symbol?.Family?.Name ?? "Unknown";
                    typeName = fi.Symbol?.Name ?? "";
                }
                else
                {
                    // System families: the family name is a parameter on the type element
                    var typeId = elem.GetTypeId();
                    var typeElem = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) : null;
                    var famParam = typeElem?.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
                    famName = famParam?.AsString() ?? elem.Category?.Name ?? "Other";
                    typeName = typeElem?.Name ?? "";
                }

                // Instance label: Mark parameter if available, else element ID
                var mark = elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                string instanceLabel = !string.IsNullOrEmpty(mark) ? mark : $"#{id.Value}";

                elements.Add(new ElementData
                {
                    ElementId = id.Value,
                    CategoryName = catName,
                    FamilyName = famName,
                    TypeName = typeName,
                    InstanceLabel = instanceLabel
                });
            }

            var roots = SelectionFilterNode.Build(elements);

            Dispatcher.Invoke(() =>
            {
                _filterRoots = roots;
                _filterDrillNodes.Clear();

                App.ViewModel.EnterReportMode(
                    BuildFilterPage(null), "FILTER", DrillFilterNode, accent: "Filter");
            });
        });
    }

    private int FilterTotalCount => _filterRoots.Sum(n => n.TotalCount);

    private int FilterTickedCount => _filterRoots.Sum(n => n.GetCheckedLeafIds().Count);

    /// <summary>"Selection > Walls > Basic Wall" for a node, built by walking up its parents.</summary>
    private static string FilterPath(SelectionFilterNode? node)
    {
        var parts = new List<string>();
        for (var n = node; n != null; n = n.Parent)
            parts.Insert(0, n.Name);
        parts.Insert(0, "Selection");
        return string.Join(" > ", parts);
    }

    /// <summary>
    /// One level of the tree as a report page. <paramref name="parent"/> is
    /// null for the top level (the categories).
    /// </summary>
    private ReportPage BuildFilterPage(SelectionFilterNode? parent)
    {
        var nodes = parent?.Children ?? _filterRoots;

        var page = new ReportPage
        {
            BackLabel = parent == null ? null : (parent.Parent?.Name ?? "Selection"),
            PrimaryActionKey = FilterApplyKey,
            PrimaryActionLabel = "apply",
            SetAllChecks = select =>
            {
                foreach (var node in nodes) node.SetChecked(select);
            },
        };

        var applyRow = new ReportRow
        {
            Text = "Apply to selection",
            Value = "Ctrl+Enter",
            ActionKey = FilterApplyKey,
            GroupKey = "ACTIONS",
        };
        page.Rows.Add(applyRow);

        // Remember which node each row stands for, so the page can re-read the
        // tick states every time it is redrawn.
        var rowNodes = new List<(ReportRow Row, SelectionFilterNode Node)>();

        foreach (var node in nodes)
        {
            var current = node; // captured by the lambda below
            bool isInstance = node.Level == SelectionFilterLevel.Instance;

            var row = new ReportRow
            {
                Text = isInstance
                    ? (node.InstanceLabel ?? $"#{node.LeafElementIds.FirstOrDefault()}")
                    : $"{node.Name} ({node.TotalCount})",
                Value = node.Level switch
                {
                    SelectionFilterLevel.Category => Plural(node.Children.Count, "family", "families"),
                    SelectionFilterLevel.Family => Plural(node.Children.Count, "type", "types"),
                    SelectionFilterLevel.Type => Plural(node.TotalCount, "instance", "instances"),
                    _ => $"#{node.LeafElementIds.FirstOrDefault()}",
                },
                IsCheckable = true,
                GroupKey = node.Level switch
                {
                    SelectionFilterLevel.Category => "CATEGORIES",
                    SelectionFilterLevel.Family => "FAMILIES",
                    SelectionFilterLevel.Type => "TYPES",
                    _ => "INSTANCES",
                },
                OnToggle = _ => current.Toggle(),
            };

            if (node.Children.Count > 0)
            {
                row.DrillKey = "sf:" + _filterDrillNodes.Count;
                _filterDrillNodes[row.DrillKey] = node;
            }

            page.Rows.Add(row);
            rowNodes.Add((row, node));
        }

        // Start on the first thing to tick, not on the Apply row above it.
        if (rowNodes.Count > 0) page.InitialCursorRow = rowNodes[0].Row;

        page.Recalculate = p =>
        {
            int ticked = FilterTickedCount;

            p.Breadcrumb = $"{FilterPath(parent)} · {ticked} of {FilterTotalCount} ticked";

            applyRow.Label = ticked == 0
                ? "Nothing ticked yet. Space ticks the highlighted row"
                : $"Keeps {Plural(ticked, "element", "elements")} selected and closes";

            foreach (var (row, node) in rowNodes)
                row.IsChecked = node.IsChecked;
        };

        return page;
    }

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    /// <summary>Enter / Right on a filter row: the level below it.</summary>
    private ReportPage? DrillFilterNode(ReportRow row)
    {
        if (row.DrillKey == null) return null;
        return _filterDrillNodes.TryGetValue(row.DrillKey, out var node)
            ? BuildFilterPage(node)
            : null;
    }

    /// <summary>
    /// Makes the ticked elements the Revit selection and closes the pane.
    /// </summary>
    private void ApplySelectionFilter()
    {
        var checkedIds = new HashSet<long>();
        foreach (var root in _filterRoots)
            checkedIds.UnionWith(root.GetCheckedLeafIds());

        if (checkedIds.Count == 0)
        {
            App.ViewModel.ShowInlineError("Nothing ticked. Space ticks the highlighted row");
            return;
        }

        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            uidoc.Selection.SetElementIds(checkedIds.Select(id => new ElementId(id)).ToList());
            FinderCommand.ClosePane(uiApp);
        });
    }
}
