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
    /// Runs a one-shot quip (one that does its job and is done, with no screen
    /// of its own in the pane). Must be called on the Revit API thread.
    ///
    /// Returns whether it worked plus a short message. The caller closes the
    /// pane on success and keeps it open, showing the message, on failure.
    /// Interactive quips (sf, pi, pf, dwg, lp, ut, us) are launched by the
    /// view instead and never reach this method.
    /// </summary>
    public static (bool Ok, string Message) Execute(string shebangId, UIApplication uiApp)
    {
        return shebangId switch
        {
            "pu" => ToggleProjectUnits(uiApp),
            "pin" => PinAllLevelsGridsLinks(uiApp),
            "gv" => ToggleGroupVisibility(uiApp),
            _ => (false, $"Unknown quip \"{shebangId}\""),
        };
    }

    private static readonly (bool, string) NoDocument = (false, "Open a project first");

    /// <summary>
    /// Toggles project units between imperial (feet/fractional inches) and SI (mm/m²/m³).
    /// Detects the current system by majority vote, then switches to the opposite.
    /// </summary>
    private static (bool Ok, string Message) ToggleProjectUnits(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc == null) return NoDocument;

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

        return (true, switchToMetric ? "Units set to metric" : "Units set to imperial");
    }

    /// <summary>
    /// Pins all Levels, Grids, and RevitLinkInstances in the project.
    /// </summary>
    private static (bool Ok, string Message) PinAllLevelsGridsLinks(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc == null) return NoDocument;

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

        return (true, $"Pinned {pinned} element{(pinned == 1 ? "" : "s")}");
    }

    /// <summary>
    /// Toggles temporary visibility of everything belonging to a group in the active view.
    /// First run: temporarily hides all Group elements and their members.
    /// Second run: clears the view's Temporary Hide/Isolate mode (same as the
    /// "Reset Temporary Hide/Isolate" button on the view control bar).
    /// </summary>
    private static (bool Ok, string Message) ToggleGroupVisibility(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        var view = doc?.ActiveView;
        if (doc == null || view == null) return NoDocument;

        // Views that don't support temporary hide (sheets, schedules, browsers).
        if (!view.CanUseTemporaryVisibilityModes())
            return (false, "This view can't temporarily hide elements");

        // If any temporary hide/isolate is already on, just clear it — this is the
        // "toggle off" half, and it matches what the manual reset button does.
        if (view.IsTemporaryHideIsolateActive())
        {
            using var reset = new Transaction(doc, "Quip: Show Groups");
            reset.Start();
            view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            reset.Commit();
            return (true, "Groups shown again");
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
        if (toHide.Count == 0) return (false, "No groups in this view");

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

        if (hideable.Count == 0) return (false, "The groups in this view can't be hidden");

        using var tx = new Transaction(doc, "Quip: Hide Groups");
        tx.Start();
        view.HideElementsTemporary(hideable);
        tx.Commit();

        return (true, $"Hid {groups.Count} group{(groups.Count == 1 ? "" : "s")}");
    }
}
