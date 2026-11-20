using Autodesk.Revit.DB;

namespace w_finder.Services;

/// <summary>One CAD instance found in the project, linked or imported.</summary>
public class CadEntry
{
    public string Name { get; set; } = "";
    public bool IsLinked { get; set; }
    public long ElementId { get; set; }

    /// <summary>Worksharing creator, or "N/A" in a non-workshared file.</summary>
    public string CreatedBy { get; set; } = "N/A";

    /// <summary>
    /// Workset name for a model-level CAD, or View "&lt;name&gt;" for a
    /// view-specific one (drafting views, legends, single plans).
    /// </summary>
    public string Workset { get; set; } = "N/A";

    public string HostedLevel { get; set; } = "N/A";

    /// <summary>Z offset from the hosted level, in feet. Null when unknown.</summary>
    public double? OffsetFeet { get; set; }

    /// <summary>True when the CAD belongs to one view rather than the whole model.</summary>
    public bool IsViewSpecific { get; set; }

    public string OffsetDisplay => OffsetFeet is null
        ? "N/A"
        : $"{OffsetFeet.Value:0.0} ft ({OffsetFeet.Value * 0.3048:0.0} m)";
}

/// <summary>Everything the !dwg quip found, split into the two menus.</summary>
public class CadReport
{
    public List<CadEntry> Linked { get; set; } = new();
    public List<CadEntry> Imported { get; set; } = new();

    /// <summary>CAD types still in the file with no instance placed anywhere.</summary>
    public List<(string Name, long Id)> UnplacedTypes { get; set; } = new();
}

/// <summary>
/// Read-only collectors behind the !dwg quip and the delete behind !lp.
/// Nothing here shows UI — the pane renders everything.
/// </summary>
public static class DwgCleanupService
{
    // -----------------------------------------------------------------------
    // !dwg — collect linked and imported CAD
    // -----------------------------------------------------------------------

    public static CadReport CollectCad(Document doc)
    {
        var report = new CadReport();

        // ImportInstance covers both "Link CAD" and "Import CAD" placements;
        // IsLinked is the only thing separating them. No view filter on the
        // collector, so drafting views, legends and plans are all included.
        var instances = new FilteredElementCollector(doc)
            .OfClass(typeof(ImportInstance))
            .WhereElementIsNotElementType()
            .Cast<ImportInstance>()
            .ToList();

        // Worksharing info is only available in a workshared file, and the call
        // throws otherwise — so decide once rather than per element.
        var workshared = doc.IsWorkshared;

        foreach (var inst in instances)
        {
            var entry = new CadEntry
            {
                Name = TypeNameOf(doc, inst),
                IsLinked = inst.IsLinked,
                ElementId = inst.Id.Value,
                CreatedBy = workshared ? CreatorOf(doc, inst) : "N/A",
            };

            FillPlacement(doc, inst, entry);

            if (entry.IsLinked) report.Linked.Add(entry);
            else report.Imported.Add(entry);
        }

        // A CAD can also survive as a *type* with no instance left (the graphics
        // were deleted but the type still bloats the file). Those never appear
        // in the collector above, so sweep them separately.
        var placedTypeIds = new HashSet<long>(instances.Select(i => i.GetTypeId().Value));

        report.UnplacedTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(CADLinkType))
            .Cast<CADLinkType>()
            .Where(t => !placedTypeIds.Contains(t.Id.Value))
            .Select(t => (t.Name, t.Id.Value))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        report.Linked = report.Linked.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        report.Imported = report.Imported.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return report;
    }

    /// <summary>The CAD type name, which is the original .dwg filename.</summary>
    private static string TypeNameOf(Document doc, ImportInstance inst)
    {
        var type = doc.GetElement(inst.GetTypeId());
        if (type != null && !string.IsNullOrWhiteSpace(type.Name)) return type.Name;
        return inst.Category?.Name ?? "(unnamed DWG)";
    }

    private static string CreatorOf(Document doc, Element e)
    {
        try
        {
            var info = WorksharingUtils.GetWorksharingTooltipInfo(doc, e.Id);
            return string.IsNullOrWhiteSpace(info.Creator) ? "N/A" : info.Creator;
        }
        catch
        {
            return "N/A";
        }
    }

    /// <summary>
    /// Fills workset/owner-view, hosted level and Z offset. A view-specific CAD
    /// reports the view it lives in instead of a workset, because that is the
    /// thing you actually need in order to go find it.
    /// </summary>
    private static void FillPlacement(Document doc, ImportInstance inst, CadEntry entry)
    {
        var ownerId = inst.OwnerViewId;
        entry.IsViewSpecific = ownerId != null && ownerId != ElementId.InvalidElementId;

        if (entry.IsViewSpecific)
        {
            entry.Workset = doc.GetElement(ownerId) is View v
                ? $"View \"{v.Name}\""
                : "View (unknown)";
        }
        else if (doc.IsWorkshared)
        {
            try
            {
                var ws = doc.GetWorksetTable().GetWorkset(inst.WorksetId);
                entry.Workset = ws?.Name ?? "N/A";
            }
            catch { entry.Workset = "N/A"; }
        }

        // Hosted level. A view-specific CAD is annotation — it has no level.
        Level? level = null;
        try
        {
            if (inst.LevelId != null && inst.LevelId != ElementId.InvalidElementId)
                level = doc.GetElement(inst.LevelId) as Level;
        }
        catch { /* not all import instances expose a level */ }

        entry.HostedLevel = level?.Name ?? "N/A";

        // Z offset. Taken from the instance transform rather than a
        // BuiltInParameter, because the transform is always present. Revit's
        // internal length unit is feet.
        try
        {
            var z = inst.GetTransform().Origin.Z;
            entry.OffsetFeet = level != null ? z - level.Elevation : z;
        }
        catch { entry.OffsetFeet = null; }
    }

    // -----------------------------------------------------------------------
    // !lp — IMPORT- line patterns
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every LinePatternElement named "IMPORT-*". Line *styles* (subcategories /
    /// GraphicsStyle) are deliberately not touched — they stay useful even unused.
    /// </summary>
    public static List<(string Name, long Id)> FindImportedLinePatterns(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(LinePatternElement))
            .Cast<LinePatternElement>()
            .Where(p => p.Name.StartsWith("IMPORT-", StringComparison.OrdinalIgnoreCase))
            .Select(p => (p.Name, p.Id.Value))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Deletes the given line patterns. Returns how many went, and a line per
    /// failure. Must run on the Revit API thread.
    /// </summary>
    public static (int Deleted, List<string> Failed) DeleteLinePatterns(
        Document doc, IEnumerable<long> ids)
    {
        var deleted = 0;
        var failed = new List<string>();

        using var tx = new Transaction(doc, "Quip: Clean IMPORT- Line Patterns");
        tx.Start();

        foreach (var id in ids)
        {
            var element = doc.GetElement(new ElementId(id));
            if (element == null) continue;

            // Read the name first — the element is gone after the delete.
            var name = element.Name;
            try
            {
                // One at a time, so a single protected pattern doesn't take the
                // whole batch down with it.
                doc.Delete(element.Id);
                deleted++;
            }
            catch (Exception ex)
            {
                failed.Add($"{name} — {ex.Message}");
            }
        }

        tx.Commit();
        return (deleted, failed);
    }
}
