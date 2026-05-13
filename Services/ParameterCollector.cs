using Autodesk.Revit.DB;
using w_finder.Models;

namespace w_finder.Services;

/// <summary>
/// Reads parameters from selected Revit elements and builds InspectorParameterItem lists.
/// Handles single element, multi-element same category, and multi-element mixed category.
/// Must be called on the Revit API thread.
/// </summary>
public static class ParameterCollector
{
    /// <summary>
    /// Collects parameters from the given element IDs.
    /// Returns (items, breadcrumb, isMixedCategory).
    /// </summary>
    public static (List<InspectorParameterItem> items, string breadcrumb, bool isMixedCategory)
        Collect(Document doc, ICollection<ElementId> selectedIds)
    {
        if (selectedIds.Count == 0)
            return (new List<InspectorParameterItem>(), "No selection", false);

        // Resolve elements and group by category
        var elements = new List<Element>();
        var categoryGroups = new Dictionary<string, List<Element>>();

        foreach (var id in selectedIds)
        {
            var elem = doc.GetElement(id);
            if (elem == null) continue;
            elements.Add(elem);

            string catName = elem.Category?.Name ?? "Uncategorized";
            if (!categoryGroups.ContainsKey(catName))
                categoryGroups[catName] = new List<Element>();
            categoryGroups[catName].Add(elem);
        }

        if (elements.Count == 0)
            return (new List<InspectorParameterItem>(), "No valid elements", false);

        // Single element
        if (elements.Count == 1)
        {
            var elem = elements[0];
            string typeName = GetElementTypeName(doc, elem);
            string breadcrumb = $"{elem.Category?.Name ?? "Element"} : {typeName}";
            var items = CollectSingleElement(doc, elem);
            return (items, breadcrumb, false);
        }

        // Multiple elements, check if same category
        if (categoryGroups.Count == 1)
        {
            var kvp = categoryGroups.First();
            string breadcrumb = $"{kvp.Value.Count} {kvp.Key}s";
            var items = CollectSameCategory(doc, kvp.Value);
            return (items, breadcrumb, false);
        }

        // Mixed categories — return category list for navigation
        var catItems = new List<InspectorParameterItem>();
        foreach (var kvp in categoryGroups.OrderBy(k => k.Key))
        {
            catItems.Add(new InspectorParameterItem
            {
                Name = kvp.Key,
                DisplayValue = $"{kvp.Value.Count} elements",
                IsCategoryRow = true,
                CategoryName = kvp.Key,
                CategoryElementCount = kvp.Value.Count,
                ElementIds = kvp.Value.Select(e => e.Id.Value).ToList()
            });
        }

        string mixedBreadcrumb = $"{elements.Count} elements";
        return (catItems, mixedBreadcrumb, true);
    }

    /// <summary>
    /// Collects parameters for a single category subset (used after drill-in from mixed categories).
    /// </summary>
    public static (List<InspectorParameterItem> items, string breadcrumb)
        CollectForCategory(Document doc, string categoryName, List<long> elementIds, int totalSelected)
    {
        var elements = new List<Element>();
        foreach (var id in elementIds)
        {
            var elem = doc.GetElement(new ElementId(id));
            if (elem != null) elements.Add(elem);
        }

        if (elements.Count == 0)
            return (new List<InspectorParameterItem>(), categoryName);

        if (elements.Count == 1)
        {
            string typeName = GetElementTypeName(doc, elements[0]);
            string breadcrumb = $"{categoryName} : {typeName}";
            var items = CollectSingleElement(doc, elements[0]);
            return (items, breadcrumb);
        }

        string bc = $"{categoryName} ({elements.Count} of {totalSelected})";
        var paramItems = CollectSameCategory(doc, elements);
        return (paramItems, bc);
    }

    /// <summary>
    /// Collects all instance + type parameters for a single element.
    /// </summary>
    private static List<InspectorParameterItem> CollectSingleElement(Document doc, Element elem)
    {
        var items = new List<InspectorParameterItem>();
        var elementId = elem.Id.Value;

        // Instance parameters
        int sortOrder = 0;
        foreach (var param in GetSortedParameters(elem))
        {
            items.Add(BuildItem(param, isInstance: true, sortOrder++, new List<long> { elementId }));
        }

        // Type parameters
        var typeId = elem.GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var typeElem = doc.GetElement(typeId);
            if (typeElem != null)
            {
                // Add divider
                items.Add(new InspectorParameterItem
                {
                    Name = "── Type Parameters ──",
                    IsDivider = true,
                    IsReadOnly = true,
                    GroupName = "zzz_type_divider",
                    GroupSortOrder = 999,
                    SortOrder = 0
                });

                sortOrder = 0;
                foreach (var param in GetSortedParameters(typeElem))
                {
                    items.Add(BuildItem(param, isInstance: false, sortOrder++, new List<long> { elementId }));
                }
            }
        }

        return items;
    }

    /// <summary>
    /// Collects shared parameters across multiple elements of the same category.
    /// Parameters that exist on all elements are shown; mixed values display "Varies".
    /// </summary>
    private static List<InspectorParameterItem> CollectSameCategory(Document doc, List<Element> elements)
    {
        var items = new List<InspectorParameterItem>();
        int count = elements.Count;
        var elementIds = elements.Select(e => e.Id.Value).ToList();

        // Collect instance parameters: find those shared across all elements
        var instanceParamSets = new List<Dictionary<string, Parameter>>();
        foreach (var elem in elements)
        {
            var dict = new Dictionary<string, Parameter>();
            foreach (Parameter p in elem.Parameters)
            {
                if (p.Definition == null) continue;
                dict.TryAdd(p.Definition.Name, p);
            }
            instanceParamSets.Add(dict);
        }

        // Find parameter names present in ALL elements
        var sharedNames = new HashSet<string>(instanceParamSets[0].Keys);
        for (int i = 1; i < instanceParamSets.Count; i++)
            sharedNames.IntersectWith(instanceParamSets[i].Keys);

        // Build items for shared instance parameters
        int sortOrder = 0;
        // Use the first element's parameter ordering as reference
        foreach (var param in GetSortedParameters(elements[0]))
        {
            if (param.Definition == null) continue;
            if (!sharedNames.Contains(param.Definition.Name)) continue;

            var item = BuildItem(param, isInstance: true, sortOrder++, elementIds);
            item.ApplicableElementCount = count;
            item.TotalSelectedCount = count;

            // Check if values vary across elements
            string refValue = GetDisplayValue(param);
            bool varies = false;
            for (int i = 1; i < elements.Count; i++)
            {
                if (instanceParamSets[i].TryGetValue(param.Definition.Name, out var otherParam))
                {
                    if (GetDisplayValue(otherParam) != refValue)
                    {
                        varies = true;
                        break;
                    }
                }
            }

            if (varies)
            {
                item.IsVaries = true;
                item.DisplayValue = "< Varies >";
            }

            items.Add(item);
        }

        // Type parameters (use first element's type as reference)
        var typeId = elements[0].GetTypeId();
        if (typeId != ElementId.InvalidElementId)
        {
            var typeElem = doc.GetElement(typeId);
            if (typeElem != null)
            {
                // Check if all elements share the same type
                bool sameType = elements.All(e => e.GetTypeId() == typeId);

                items.Add(new InspectorParameterItem
                {
                    Name = "── Type Parameters ──",
                    IsDivider = true,
                    IsReadOnly = true,
                    GroupName = "zzz_type_divider",
                    GroupSortOrder = 999,
                    SortOrder = 0
                });

                sortOrder = 0;
                foreach (var param in GetSortedParameters(typeElem))
                {
                    var item = BuildItem(param, isInstance: false, sortOrder++, elementIds);
                    item.ApplicableElementCount = count;
                    item.TotalSelectedCount = count;

                    if (!sameType)
                    {
                        item.IsVaries = true;
                        item.DisplayValue = "< Varies >";
                        item.IsReadOnly = true; // Can't edit type params when types differ
                    }

                    items.Add(item);
                }
            }
        }

        return items;
    }

    /// <summary>
    /// Builds an InspectorParameterItem from a Revit Parameter.
    /// </summary>
    private static InspectorParameterItem BuildItem(Parameter param, bool isInstance, int sortOrder, List<long> elementIds)
    {
        string groupName = GetGroupName(param);
        int groupOrder = GetGroupSortOrder(groupName);

        return new InspectorParameterItem
        {
            Name = param.Definition?.Name ?? "(unnamed)",
            DisplayValue = GetDisplayValue(param),
            GroupName = groupName,
            GroupSortOrder = groupOrder,
            SortOrder = sortOrder,
            IsReadOnly = param.IsReadOnly || !param.HasValue,
            IsInstance = isInstance,
            StorageType = (int)param.StorageType,
            DefinitionName = param.Definition?.Name ?? "",
            IsYesNo = IsYesNoParameter(param),
            ElementIds = elementIds,
            ApplicableElementCount = 1,
            TotalSelectedCount = 1
        };
    }

    /// <summary>
    /// Gets the display value for a parameter, preferring AsValueString for unit formatting.
    /// </summary>
    private static string GetDisplayValue(Parameter param)
    {
        if (!param.HasValue)
            return "";

        // AsValueString gives the formatted display value (with units)
        string? valueString = param.AsValueString();
        if (!string.IsNullOrEmpty(valueString))
            return valueString;

        return param.StorageType switch
        {
            Autodesk.Revit.DB.StorageType.String => param.AsString() ?? "",
            Autodesk.Revit.DB.StorageType.Integer => param.AsInteger().ToString(),
            Autodesk.Revit.DB.StorageType.Double => param.AsDouble().ToString("F4"),
            Autodesk.Revit.DB.StorageType.ElementId => FormatElementId(param),
            _ => ""
        };
    }

    private static string FormatElementId(Parameter param)
    {
        var id = param.AsElementId();
        if (id == ElementId.InvalidElementId)
            return "<None>";
        return $"id:{id.Value}";
    }

    /// <summary>
    /// Returns parameters sorted by group then definition order (matching Revit's Properties palette).
    /// </summary>
    private static List<Parameter> GetSortedParameters(Element elem)
    {
        var paramList = new List<Parameter>();
        foreach (Parameter p in elem.Parameters)
        {
            if (p.Definition == null) continue;
            paramList.Add(p);
        }

        paramList.Sort((a, b) =>
        {
            int groupCmp = GetGroupSortOrder(GetGroupName(a))
                .CompareTo(GetGroupSortOrder(GetGroupName(b)));
            if (groupCmp != 0) return groupCmp;

            // Within same group, sort by name
            return string.Compare(a.Definition.Name, b.Definition.Name, StringComparison.OrdinalIgnoreCase);
        });

        return paramList;
    }

    /// <summary>
    /// Gets the parameter group label using ForgeTypeId-based API (Revit 2025).
    /// </summary>
    private static string GetGroupName(Parameter param)
    {
        try
        {
            var groupTypeId = param.Definition.GetGroupTypeId();
            if (groupTypeId != null)
            {
                string label = LabelUtils.GetLabelForGroup(groupTypeId);
                if (!string.IsNullOrEmpty(label))
                    return label;
            }
        }
        catch
        {
            // Fallback for custom/unknown groups
        }

        return "Other";
    }

    /// <summary>
    /// Sort order for parameter groups, matching Revit's native ordering.
    /// </summary>
    private static int GetGroupSortOrder(string groupName)
    {
        return groupName switch
        {
            "Constraints" => 0,
            "Construction" => 1,
            "Dimensions" => 2,
            "Identity Data" => 3,
            "Graphics" => 4,
            "Phasing" => 5,
            "Structural" => 6,
            "Analytical Properties" => 7,
            "Energy Analysis" => 8,
            "Mechanical" => 9,
            "Electrical" => 10,
            "Plumbing" => 11,
            "Other" => 12,
            _ => 13 // Custom groups
        };
    }

    /// <summary>
    /// Detects if a parameter is a Yes/No type.
    /// In Revit 2025, check the SpecTypeId against SpecTypeId.Boolean.YesNo.
    /// </summary>
    private static bool IsYesNoParameter(Parameter param)
    {
        if (param.StorageType != Autodesk.Revit.DB.StorageType.Integer)
            return false;

        try
        {
            var specTypeId = param.Definition.GetDataType();
            return specTypeId == SpecTypeId.Boolean.YesNo;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets a display name for the element's type (e.g. "Basic Wall : Generic - 200mm").
    /// </summary>
    private static string GetElementTypeName(Document doc, Element elem)
    {
        var typeId = elem.GetTypeId();
        if (typeId == ElementId.InvalidElementId)
            return elem.Name ?? "Element";

        var typeElem = doc.GetElement(typeId);
        if (typeElem == null)
            return elem.Name ?? "Element";

        // Try to get "Family Name : Type Name" format
        var famParam = typeElem.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
        string? famName = famParam?.AsString();

        if (!string.IsNullOrEmpty(famName))
            return $"{famName} : {typeElem.Name}";

        return typeElem.Name ?? elem.Name ?? "Element";
    }
}
