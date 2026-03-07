using Autodesk.Revit.DB;
using w_finder.Models;

namespace w_finder.Services;

/// <summary>
/// Collects all project-browser-style items from the active Revit document
/// and returns them as a flat list of BrowserItem objects.
/// </summary>
public static class BrowserItemCollector
{
    public static List<BrowserItem> Collect(Document doc)
    {
        var items = new List<BrowserItem>();

        CollectViews(doc, items);
        CollectSheets(doc, items);
        CollectSchedules(doc, items);
        CollectFamilies(doc, items);
        CollectGroups(doc, items);
        CollectRevitLinks(doc, items);
        CollectAssemblies(doc, items);

        return items;
    }

    private static void CollectViews(Document doc, List<BrowserItem> items)
    {
        // Get all views that are not templates and not system-internal
        var views = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate && v.CanBePrinted);

        foreach (var view in views)
        {
            items.Add(new BrowserItem
            {
                Name = view.Name,
                Category = GetViewCategory(view),
                ElementId = view.Id.Value,
                Kind = BrowserItemKind.View
            });
        }
    }

    private static string GetViewCategory(View view)
    {
        return view.ViewType switch
        {
            ViewType.FloorPlan => "Floor Plan",
            ViewType.CeilingPlan => "Ceiling Plan",
            ViewType.EngineeringPlan => "Structural Plan",
            ViewType.AreaPlan => "Area Plan",
            ViewType.Elevation => "Elevation",
            ViewType.Section => "Section",
            ViewType.Detail => "Detail View",
            ViewType.ThreeD => "3D View",
            ViewType.DraftingView => "Drafting View",
            ViewType.Legend => "Legend",
            ViewType.Rendering => "Rendering",
            ViewType.Walkthrough => "Walkthrough",
            _ => "View"
        };
    }

    private static void CollectSheets(Document doc, List<BrowserItem> items)
    {
        var sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>();

        foreach (var sheet in sheets)
        {
            items.Add(new BrowserItem
            {
                Name = $"{sheet.SheetNumber} - {sheet.Name}",
                Category = "Sheet",
                ElementId = sheet.Id.Value,
                Kind = BrowserItemKind.Sheet
            });
        }
    }

    private static void CollectSchedules(Document doc, List<BrowserItem> items)
    {
        var schedules = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(s => !s.IsTitleblockRevisionSchedule);

        foreach (var schedule in schedules)
        {
            string category = schedule.Definition.IsKeySchedule ? "Key Schedule" : "Schedule";
            items.Add(new BrowserItem
            {
                Name = schedule.Name,
                Category = category,
                ElementId = schedule.Id.Value,
                Kind = BrowserItemKind.Schedule
            });
        }
    }

    private static void CollectFamilies(Document doc, List<BrowserItem> items)
    {
        // Collect loaded families and their types
        var families = new FilteredElementCollector(doc)
            .OfClass(typeof(Family))
            .Cast<Family>();

        foreach (var family in families)
        {
            // Add the family itself
            items.Add(new BrowserItem
            {
                Name = family.Name,
                Category = family.FamilyCategory?.Name ?? "Family",
                ElementId = family.Id.Value,
                Kind = BrowserItemKind.Family
            });

            // Add each type within the family
            foreach (var typeId in family.GetFamilySymbolIds())
            {
                if (doc.GetElement(typeId) is FamilySymbol symbol)
                {
                    items.Add(new BrowserItem
                    {
                        Name = $"{family.Name}: {symbol.Name}",
                        Category = family.FamilyCategory?.Name ?? "Family Type",
                        ElementId = symbol.Id.Value,
                        Kind = BrowserItemKind.FamilyType
                    });
                }
            }
        }
    }

    private static void CollectGroups(Document doc, List<BrowserItem> items)
    {
        // Model groups
        var modelGroups = new FilteredElementCollector(doc)
            .OfClass(typeof(GroupType))
            .Cast<GroupType>()
            .Where(g => g.Category?.Id.Value == (int)BuiltInCategory.OST_IOSModelGroups);

        foreach (var group in modelGroups)
        {
            items.Add(new BrowserItem
            {
                Name = group.Name,
                Category = "Model Group",
                ElementId = group.Id.Value,
                Kind = BrowserItemKind.Group
            });
        }

        // Detail groups
        var detailGroups = new FilteredElementCollector(doc)
            .OfClass(typeof(GroupType))
            .Cast<GroupType>()
            .Where(g => g.Category?.Id.Value == (int)BuiltInCategory.OST_IOSDetailGroups);

        foreach (var group in detailGroups)
        {
            items.Add(new BrowserItem
            {
                Name = group.Name,
                Category = "Detail Group",
                ElementId = group.Id.Value,
                Kind = BrowserItemKind.Group
            });
        }
    }

    private static void CollectRevitLinks(Document doc, List<BrowserItem> items)
    {
        var links = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkType))
            .Cast<RevitLinkType>();

        foreach (var link in links)
        {
            items.Add(new BrowserItem
            {
                Name = link.Name,
                Category = "Revit Link",
                ElementId = link.Id.Value,
                Kind = BrowserItemKind.RevitLink
            });
        }
    }

    private static void CollectAssemblies(Document doc, List<BrowserItem> items)
    {
        var assemblies = new FilteredElementCollector(doc)
            .OfClass(typeof(AssemblyType))
            .Cast<AssemblyType>();

        foreach (var assembly in assemblies)
        {
            items.Add(new BrowserItem
            {
                Name = assembly.Name,
                Category = "Assembly",
                ElementId = assembly.Id.Value,
                Kind = BrowserItemKind.Assembly
            });
        }
    }
}
