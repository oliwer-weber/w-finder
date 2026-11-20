using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using w_finder.Models;

namespace w_finder.Services;

/// <summary>
/// Registry and executor for shebang commands (! prefix).
/// Each shebang is a custom plugin command exposed as a BrowserItem.
/// </summary>
public static class ShebangService
{
    private static List<BrowserItem>? _cached;

    // Each shebang has a fixed negative ElementId. Favorites are stored by that
    // id, so it must never change: give new shebangs the next unused number and
    // never reuse the number of a removed one (-1006 was the old "qa" audit).
    private static readonly (string id, string name, string category, long elementId)[] Shebangs =
    {
        ("pu", "Toggle Project Units (Imperial \u2194 SI)", "Units", -1000),
        ("pin", "Pin All Levels, Grids & Links", "Model", -1001),
        ("sf", "Selection Filter", "Selection", -1002),
        ("pi", "Property Inspector", "Properties", -1003),
        ("pf", "Parameter Filter", "Selection", -1004),
        ("gv", "Toggle Group Visibility (temporary hide)", "View", -1005),
        ("dwg", "List Imported DWGs (not linked)", "Audit", -1007),
        ("lp", "Clean Imported Line Patterns (IMPORT-*)", "Cleanup", -1008),
        ("ut", "Find Unused Types (0 instances)", "Cleanup", -1009),
        ("us", "Delete Unused Types of Selection", "Cleanup", -1010),
    };

    /// <summary>
    /// Returns all registered shebangs as BrowserItems.
    /// Cached after first call.
    /// </summary>
    public static List<BrowserItem> Collect()
    {
        if (_cached != null) return _cached;

        var items = new List<BrowserItem>();

        foreach (var (id, name, category, elementId) in Shebangs)
        {
            items.Add(new BrowserItem
            {
                Name = name,
                Category = category,
                ElementId = elementId,
                Kind = BrowserItemKind.Shebang,
                CommandName = id,
            });
        }

        _cached = items;
        return _cached;
    }

    /// <summary>
    /// Executes a shebang by its ID. Must be called on the Revit API thread.
    /// </summary>
    public static void Execute(string shebangId, UIApplication uiApp)
    {
        switch (shebangId)
        {
            case "pu":
                ToggleProjectUnits(uiApp);
                break;
            case "pin":
                PinAllLevelsGridsLinks(uiApp);
                break;
            case "sf":
                // Interactive filter — handled by code-behind, not here.
                break;
            case "pi":
                // Interactive inspector — handled by code-behind, not here.
                break;
            case "pf":
                // Interactive parameter filter — handled by code-behind, not here.
                break;
            case "gv":
                ToggleGroupVisibility(uiApp);
                break;
            case "dwg":
            case "lp":
            case "ut":
            case "us":
                // All render inside the pane (LaunchCadReport,
                // LaunchLinePatternCleanup, LaunchUnusedTypes in the
                // code-behind), so there is nothing to do on the Revit thread here.
                break;
        }
    }

    /// <summary>
    /// Toggles project units between imperial (feet/fractional inches) and SI (mm/m²/m³).
    /// Detects the current system by majority vote, then switches to the opposite.
    /// </summary>
    private static void ToggleProjectUnits(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc == null) return;

        var units = doc.GetUnits();

        // Key specs we want to toggle
        var specMap = new Dictionary<ForgeTypeId, (ForgeTypeId imperial, ForgeTypeId metric)>
        {
            [SpecTypeId.Length]  = (UnitTypeId.FeetFractionalInches, UnitTypeId.Millimeters),
            [SpecTypeId.Area]   = (UnitTypeId.SquareFeet, UnitTypeId.SquareMeters),
            [SpecTypeId.Volume] = (UnitTypeId.CubicFeet, UnitTypeId.CubicMeters),
        };

        // Detect current system by majority vote
        int imperialCount = 0;
        int metricCount = 0;

        foreach (var (spec, (imperial, metric)) in specMap)
        {
            try
            {
                var current = units.GetFormatOptions(spec).GetUnitTypeId();
                if (current == imperial) imperialCount++;
                else if (current == metric) metricCount++;
            }
            catch { /* spec not available in this project */ }
        }

        bool switchToMetric = imperialCount >= metricCount;

        using (var tx = new Transaction(doc, "Quip: Toggle Units"))
        {
            tx.Start();

            foreach (var (spec, (imperial, metric)) in specMap)
            {
                try
                {
                    var fo = new FormatOptions();
                    fo.UseDefault = false;
                    fo.SetUnitTypeId(switchToMetric ? metric : imperial);

                    // Set accuracy for imperial length to 1/16"
                    if (!switchToMetric && spec == SpecTypeId.Length)
                        fo.Accuracy = 0.0625; // 1/16"

                    units.SetFormatOptions(spec, fo);
                }
                catch { /* skip specs that fail */ }
            }

            doc.SetUnits(units);
            tx.Commit();
        }
    }

    /// <summary>
    /// Pins all Levels, Grids, and RevitLinkInstances in the project.
    /// </summary>
    private static void PinAllLevelsGridsLinks(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc == null) return;

        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .ToElements();

        var grids = new FilteredElementCollector(doc)
            .OfClass(typeof(Grid))
            .ToElements();

        var links = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .ToElements();

        int pinned = 0;

        using (var tx = new Transaction(doc, "Quip: Pin Levels, Grids & Links"))
        {
            tx.Start();

            foreach (var element in levels.Concat(grids).Concat(links))
            {
                if (!element.Pinned)
                {
                    element.Pinned = true;
                    pinned++;
                }
            }

            tx.Commit();
        }
    }

    /// <summary>
    /// Toggles temporary visibility of everything belonging to a group in the active view.
    /// First run: temporarily hides all Group elements and their members.
    /// Second run: clears the view's Temporary Hide/Isolate mode (same as the
    /// "Reset Temporary Hide/Isolate" button on the view control bar).
    /// </summary>
    private static void ToggleGroupVisibility(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        var view = doc?.ActiveView;
        if (doc == null || view == null) return;

        // Views that don't support temporary hide (sheets, schedules, browsers).
        if (!view.CanUseTemporaryVisibilityModes()) return;

        // If any temporary hide/isolate is already on, just clear it — this is the
        // "toggle off" half, and it matches what the manual reset button does.
        if (view.IsTemporaryHideIsolateActive())
        {
            using var reset = new Transaction(doc, "Quip: Show Groups");
            reset.Start();
            view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            reset.Commit();
            return;
        }

        // Collect group instances visible in this view, plus their members.
        // OfClass(Group) covers both model groups and detail groups.
        var groups = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Group))
            .Cast<Group>()
            .ToList();

        var toHide = new List<ElementId>();

        foreach (var g in groups)
        {
            toHide.Add(g.Id);
            foreach (var memberId in g.GetMemberIds())
                toHide.Add(memberId);
        }

        // Nothing grouped in this view — leave the view untouched.
        if (toHide.Count == 0) return;

        // Elements that can't be hidden (pinned-in-place category exceptions, etc.)
        // would throw, so filter down to hideable ones first.
        var hideable = toHide
            .Where(id =>
            {
                var e = doc.GetElement(id);
                return e != null && e.CanBeHidden(view);
            })
            .Distinct()
            .ToList();

        if (hideable.Count == 0) return;

        using var tx = new Transaction(doc, "Quip: Hide Groups");
        tx.Start();
        view.HideElementsTemporary(hideable);
        tx.Commit();
    }
}
