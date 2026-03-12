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

    private static readonly (string id, string name, string category)[] Shebangs =
    {
        ("pu", "Toggle Project Units (Imperial \u2194 SI)", "Units"),
        ("pin", "Pin All Levels, Grids & Links", "Model"),
    };

    /// <summary>
    /// Returns all registered shebangs as BrowserItems.
    /// Cached after first call.
    /// </summary>
    public static List<BrowserItem> Collect()
    {
        if (_cached != null) return _cached;

        var items = new List<BrowserItem>();
        long negId = -1000;

        foreach (var (id, name, category) in Shebangs)
        {
            items.Add(new BrowserItem
            {
                Name = name,
                Category = category,
                ElementId = negId--,
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

        using (var tx = new Transaction(doc, "Rauncher: Toggle Units"))
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

        using (var tx = new Transaction(doc, "Rauncher: Pin Levels, Grids & Links"))
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
}
