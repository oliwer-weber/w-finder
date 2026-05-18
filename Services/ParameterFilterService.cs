using Autodesk.Revit.DB;

namespace w_finder.Services;

/// <summary>
/// Scope used by the Parameter Filter quip. Entire Project enumerates the whole
/// document; Active View restricts to elements visible in the user's current view.
/// </summary>
public enum ParamFilterScope
{
    EntireProject,
    ActiveView
}

/// <summary>
/// Match rule applied to the selected parameter's value.
/// </summary>
public enum ParamFilterRule
{
    Contains,
    DoesNotContain,
    Equals,
    DoesNotEqual,
    StartsWith,
    EndsWith,
    IsEmpty,
    IsNotEmpty
}

public static class ParamFilterRuleExt
{
    public static string Label(this ParamFilterRule rule) => rule switch
    {
        ParamFilterRule.Contains => "contains",
        ParamFilterRule.DoesNotContain => "does not contain",
        ParamFilterRule.Equals => "equals",
        ParamFilterRule.DoesNotEqual => "does not equal",
        ParamFilterRule.StartsWith => "starts with",
        ParamFilterRule.EndsWith => "ends with",
        ParamFilterRule.IsEmpty => "is empty",
        ParamFilterRule.IsNotEmpty => "is not empty",
        _ => "?"
    };

    /// <summary>True if this rule needs a Value field; false for is-empty/is-not-empty.</summary>
    public static bool TakesValue(this ParamFilterRule rule)
        => rule != ParamFilterRule.IsEmpty && rule != ParamFilterRule.IsNotEmpty;

    public static ParamFilterRule? FromLabel(string? label) => label?.ToLowerInvariant() switch
    {
        "contains" => ParamFilterRule.Contains,
        "does not contain" => ParamFilterRule.DoesNotContain,
        "equals" => ParamFilterRule.Equals,
        "does not equal" => ParamFilterRule.DoesNotEqual,
        "starts with" => ParamFilterRule.StartsWith,
        "ends with" => ParamFilterRule.EndsWith,
        "is empty" => ParamFilterRule.IsEmpty,
        "is not empty" => ParamFilterRule.IsNotEmpty,
        _ => null
    };
}

public static class ParamFilterScopeExt
{
    public static string Label(this ParamFilterScope scope) => scope switch
    {
        ParamFilterScope.EntireProject => "Entire Project",
        ParamFilterScope.ActiveView => "Active View",
        _ => "?"
    };

    public static ParamFilterScope? FromLabel(string? label) => label switch
    {
        "Entire Project" => ParamFilterScope.EntireProject,
        "Active View" => ParamFilterScope.ActiveView,
        _ => null
    };
}

/// <summary>
/// Builds parameter indices and executes parameter-based selection queries.
/// All methods must be called on the Revit API thread.
/// </summary>
public static class ParameterFilterService
{
    /// <summary>
    /// Distinct parameter display names present on any element in the chosen scope.
    /// </summary>
    public static List<string> CollectParameterNames(Document doc, ParamFilterScope scope, View? activeView)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in EnumerateScope(doc, scope, activeView))
        {
            foreach (Parameter p in element.Parameters)
            {
                var name = p?.Definition?.Name;
                if (!string.IsNullOrEmpty(name)) names.Add(name!);
            }
        }
        var list = names.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>
    /// Distinct displayed value strings for the given parameter across the scope.
    /// Used as suggestions in the Value field's dropdown.
    /// </summary>
    public static List<string> CollectValuesFor(Document doc, string parameterName, ParamFilterScope scope, View? activeView)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in EnumerateScope(doc, scope, activeView))
        {
            var p = element.LookupParameter(parameterName);
            if (p == null) continue;
            var val = GetParamValueString(p);
            if (!string.IsNullOrEmpty(val)) values.Add(val!);
        }
        var list = values.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>
    /// Returns the ElementIds whose parameter value matches the rule against the query.
    /// </summary>
    public static List<ElementId> Execute(Document doc, ParamFilterScope scope, View? activeView,
        string parameterName, ParamFilterRule rule, string value)
    {
        var matches = new List<ElementId>();
        var q = value.Trim();
        foreach (var element in EnumerateScope(doc, scope, activeView))
        {
            var p = element.LookupParameter(parameterName);
            if (p == null) continue;
            var v = GetParamValueString(p) ?? string.Empty;
            if (EvaluateRule(v, rule, q))
                matches.Add(element.Id);
        }
        return matches;
    }

    private static IEnumerable<Element> EnumerateScope(Document doc, ParamFilterScope scope, View? activeView)
    {
        FilteredElementCollector collector;
        if (scope == ParamFilterScope.ActiveView && activeView != null && !activeView.IsTemplate)
        {
            try
            {
                collector = new FilteredElementCollector(doc, activeView.Id);
            }
            catch
            {
                // Some view kinds throw — fall back to whole project.
                collector = new FilteredElementCollector(doc);
            }
        }
        else
        {
            collector = new FilteredElementCollector(doc);
        }

        // Instances only — filtering on FamilySymbols/Types just clutters results.
        return collector.WhereElementIsNotElementType();
    }

    /// <summary>
    /// Best-effort human-readable value for a parameter. Prefers AsValueString
    /// (carries units), falls back to storage-type-specific reads.
    /// </summary>
    private static string? GetParamValueString(Parameter p)
    {
        var s = p.AsValueString();
        if (!string.IsNullOrEmpty(s)) return s;
        return p.StorageType switch
        {
            StorageType.String => p.AsString(),
            StorageType.Integer => p.AsInteger().ToString(),
            StorageType.Double => p.AsDouble().ToString(),
            StorageType.ElementId => p.AsElementId().Value.ToString(),
            _ => null
        };
    }

    private static bool EvaluateRule(string fieldValue, ParamFilterRule rule, string query)
    {
        var c = StringComparison.OrdinalIgnoreCase;
        return rule switch
        {
            ParamFilterRule.IsEmpty => string.IsNullOrWhiteSpace(fieldValue),
            ParamFilterRule.IsNotEmpty => !string.IsNullOrWhiteSpace(fieldValue),
            ParamFilterRule.Contains => fieldValue.IndexOf(query, c) >= 0,
            ParamFilterRule.DoesNotContain => fieldValue.IndexOf(query, c) < 0,
            ParamFilterRule.Equals => fieldValue.Equals(query, c),
            ParamFilterRule.DoesNotEqual => !fieldValue.Equals(query, c),
            ParamFilterRule.StartsWith => fieldValue.StartsWith(query, c),
            ParamFilterRule.EndsWith => fieldValue.EndsWith(query, c),
            _ => false
        };
    }
}
