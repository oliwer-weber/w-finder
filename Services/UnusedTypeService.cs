using Autodesk.Revit.DB;

namespace w_finder.Services;

/// <summary>One element type with no instances placed in the model.</summary>
public class UnusedType
{
    public long ElementId { get; set; }
    public string CategoryName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string TypeName { get; set; } = "";

    /// <summary>"Single-Flush: 36x84" — what the user sees in the browser.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(FamilyName)
        ? TypeName
        : $"{FamilyName}: {TypeName}";
}

/// <summary>Result of a scan: what's unused, and how many types there were in total.</summary>
public class UnusedTypeScan
{
    public List<UnusedType> Unused { get; set; } = new();

    /// <summary>All model-category types considered, used or not.</summary>
    public int TotalTypes { get; set; }
}

/// <summary>
/// Narrows a scan to the "sibling" types of some starting elements (!us).
///
/// Holds plain keys rather than Element references, so it stays valid after
/// the selection changes or the selected elements are deleted. That is what
/// lets the rescan after a delete stay scoped to the original selection.
/// </summary>
public class UnusedTypeScope
{
    private readonly HashSet<string> _keys = new();

    /// <summary>True when nothing in the selection had a usable type.</summary>
    public bool IsEmpty => _keys.Count == 0;

    /// <summary>
    /// Builds the scope from selected element IDs. Must run on the Revit API thread.
    ///
    ///   Family instance, family type, or a Family itself → every type of that Family.
    ///   System element or system type (walls, floors, ducts, ...) → every type in
    ///   the same category with the same FamilyName, e.g. all "Basic Wall" types.
    ///
    /// Elements with no type, or a type with no category, are skipped.
    /// </summary>
    public static UnusedTypeScope FromSelection(Document doc, IEnumerable<ElementId> ids)
    {
        var scope = new UnusedTypeScope();

        foreach (var id in ids)
        {
            var e = doc.GetElement(id);
            if (e == null) continue;

            // A Family picked in the project browser: it has no type of its own,
            // but its symbols are exactly the siblings we want.
            if (e is Family family)
            {
                scope._keys.Add("F:" + family.Id.Value);
                continue;
            }

            // A type picked directly (e.g. in the project browser), or the type
            // behind a placed element.
            var type = e as ElementType;
            if (type == null)
            {
                var typeId = e.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) continue;
                type = doc.GetElement(typeId) as ElementType;
            }
            if (type == null) continue;

            var key = KeyOf(type);
            if (key != null) scope._keys.Add(key);
        }

        return scope;
    }

    /// <summary>True if this type is a sibling of something in the selection.</summary>
    public bool Contains(ElementType type)
    {
        var key = KeyOf(type);
        return key != null && _keys.Contains(key);
    }

    /// <summary>
    /// The sibling-group key for a type. Two types share a key exactly when
    /// they belong to the same loadable Family, or to the same system family
    /// (category + FamilyName). Building and checking both go through here, so
    /// the two sides can never disagree.
    /// </summary>
    private static string? KeyOf(ElementType type)
    {
        // Loadable families. Some system types also derive from FamilySymbol
        // (mullion and panel types, for example); if they have no Family they
        // fall through to the system-family key below.
        if (type is FamilySymbol symbol)
        {
            Family? owner = null;
            try { owner = symbol.Family; } catch { /* no owning family */ }
            if (owner != null) return "F:" + owner.Id.Value;
        }

        var cat = type.Category;
        if (cat == null) return null;

        string familyName;
        try { familyName = type.FamilyName ?? ""; } catch { familyName = ""; }

        // No family name to group by: the type is only its own sibling. It is
        // in use by the selection anyway, so this never yields a candidate.
        if (string.IsNullOrWhiteSpace(familyName)) return "T:" + type.Id.Value;

        return "S:" + cat.Id.Value + ":" + familyName;
    }
}

/// <summary>
/// Finds element types in model categories that have zero instances.
///
/// Deliberately limited to <see cref="CategoryType.Model"/>, so view templates,
/// view filters, annotation types and other view-side settings are never touched.
/// </summary>
public static class UnusedTypeService
{
    /// <summary>
    /// Scans the whole project (!ut), or only the sibling types in
    /// <paramref name="scope"/> (!us). "Used" is always decided against the whole
    /// project, so a scoped scan reports exactly the subset a full scan would.
    /// </summary>
    public static UnusedTypeScan Scan(Document doc, UnusedTypeScope? scope = null)
    {
        var scan = new UnusedTypeScan();

        // 1. Every type that at least one placed element points at. This is the
        //    check that matters: doc.Delete on a type that still has instances
        //    would take the instances with it.
        var usedTypeIds = new HashSet<long>();

        foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            var typeId = e.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
                usedTypeIds.Add(typeId.Value);
        }

        // 2. Types referenced by *other types* through an ElementId parameter —
        //    a curtain wall type pointing at its panel and mullion types, a stair
        //    type pointing at its run type, and so on. Those have no instances of
        //    their own but are very much in use.
        //
        //    Only element types are swept here, not every element in the model:
        //    the instance side is already covered by GetTypeId() above, and
        //    walking parameters of a whole project's elements would be slow.
        var allTypes = new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .ToList();

        foreach (var t in allTypes)
        {
            foreach (Parameter p in t.Parameters)
            {
                if (p.StorageType != StorageType.ElementId) continue;
                var id = p.AsElementId();
                if (id != null && id != ElementId.InvalidElementId)
                    usedTypeIds.Add(id.Value);
            }
        }

        // 3. Anything left in a model category with no reference is unused.
        foreach (var t in allTypes)
        {
            var cat = t.Category;
            if (cat == null) continue;
            if (cat.CategoryType != CategoryType.Model) continue;

            // !us: only the selection's siblings count, in the total as well.
            if (scope != null && !(t is ElementType et && scope.Contains(et))) continue;

            scan.TotalTypes++;

            if (usedTypeIds.Contains(t.Id.Value)) continue;

            scan.Unused.Add(new UnusedType
            {
                ElementId = t.Id.Value,
                CategoryName = cat.Name,
                FamilyName = FamilyNameOf(t),
                TypeName = string.IsNullOrWhiteSpace(t.Name) ? "(unnamed)" : t.Name,
            });
        }

        scan.Unused = scan.Unused
            .OrderBy(u => u.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return scan;
    }

    /// <summary>
    /// The family name for a loadable family, or the system family name.
    /// ElementType exposes both through FamilyName; it can be empty for some
    /// system types, in which case the type name stands alone.
    /// </summary>
    private static string FamilyNameOf(Element t)
    {
        try
        {
            return t is ElementType et ? et.FamilyName ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Deletes the given types. Returns how many went and a line per failure.
    /// Must run on the Revit API thread.
    /// </summary>
    public static (int Deleted, List<string> Failed) Delete(Document doc, IEnumerable<long> ids)
    {
        var deleted = 0;
        var failed = new List<string>();

        using var tx = new Transaction(doc, "Quip: Delete Unused Types");
        tx.Start();

        foreach (var id in ids)
        {
            var element = doc.GetElement(new ElementId(id));

            // Already gone — deleting a family's last symbol takes the whole
            // family, and its other symbols with it.
            if (element == null) continue;

            var name = element.Name;
            try
            {
                // One at a time: the last type of a system family can't be
                // deleted, and that shouldn't abort the rest of the batch.
                doc.Delete(element.Id);
                deleted++;
            }
            catch (Exception ex)
            {
                failed.Add($"{name}: {ex.Message}");
            }
        }

        tx.Commit();
        return (deleted, failed);
    }
}
