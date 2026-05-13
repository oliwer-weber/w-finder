using Autodesk.Revit.DB;
using w_finder.Models;

namespace w_finder.Services;

/// <summary>
/// Validates and writes parameter values back to Revit elements.
/// Each edit is wrapped in a named Transaction for native Ctrl+Z undo support.
/// Must be called on the Revit API thread.
/// </summary>
public static class ParameterWriter
{
    /// <summary>
    /// Writes a new value to the given parameter on all applicable elements.
    /// Returns (success, message) — message is for user status display.
    /// </summary>
    public static (bool success, string message) Write(
        Document doc,
        InspectorParameterItem paramItem,
        string newValue)
    {
        if (paramItem.IsReadOnly)
            return (false, "Read-only parameter");

        if (paramItem.ElementIds.Count == 0)
            return (false, "No target elements");

        string txName = $"Quip: Set {paramItem.Name}";
        int updated = 0;

        using (var tx = new Transaction(doc, txName))
        {
            tx.Start();

            try
            {
                foreach (var elemId in paramItem.ElementIds)
                {
                    var elem = doc.GetElement(new ElementId(elemId));
                    if (elem == null) continue;

                    // For type parameters, get the type element
                    Element targetElem;
                    if (paramItem.IsTypeParameter)
                    {
                        var typeId = elem.GetTypeId();
                        if (typeId == ElementId.InvalidElementId) continue;
                        targetElem = doc.GetElement(typeId);
                        if (targetElem == null) continue;
                    }
                    else
                    {
                        targetElem = elem;
                    }

                    // Find the parameter by definition name
                    Parameter? param = FindParameter(targetElem, paramItem.DefinitionName);
                    if (param == null || param.IsReadOnly) continue;

                    bool ok = SetParameterValue(param, newValue, paramItem);
                    if (ok) updated++;
                }

                if (updated == 0)
                {
                    tx.RollBack();
                    return (false, "No parameters could be updated");
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                return (false, $"Error: {ex.Message}");
            }
        }

        if (updated == 1)
            return (true, "Updated");
        return (true, $"Updated {updated} elements");
    }

    /// <summary>
    /// Toggles a Yes/No parameter value on all applicable elements.
    /// </summary>
    public static (bool success, string message) ToggleYesNo(
        Document doc,
        InspectorParameterItem paramItem)
    {
        if (!paramItem.IsYesNo || paramItem.IsReadOnly)
            return (false, "Not a toggleable parameter");

        string txName = $"Quip: Toggle {paramItem.Name}";
        int updated = 0;

        using (var tx = new Transaction(doc, txName))
        {
            tx.Start();

            try
            {
                foreach (var elemId in paramItem.ElementIds)
                {
                    var elem = doc.GetElement(new ElementId(elemId));
                    if (elem == null) continue;

                    Element targetElem;
                    if (paramItem.IsTypeParameter)
                    {
                        var typeId = elem.GetTypeId();
                        if (typeId == ElementId.InvalidElementId) continue;
                        targetElem = doc.GetElement(typeId);
                        if (targetElem == null) continue;
                    }
                    else
                    {
                        targetElem = elem;
                    }

                    Parameter? param = FindParameter(targetElem, paramItem.DefinitionName);
                    if (param == null || param.IsReadOnly) continue;

                    int currentVal = param.AsInteger();
                    param.Set(currentVal == 1 ? 0 : 1);
                    updated++;
                }

                if (updated == 0)
                {
                    tx.RollBack();
                    return (false, "Could not toggle parameter");
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                return (false, $"Error: {ex.Message}");
            }
        }

        if (updated == 1)
            return (true, "Toggled");
        return (true, $"Toggled on {updated} elements");
    }

    /// <summary>
    /// Sets a parameter value based on its storage type.
    /// </summary>
    private static bool SetParameterValue(Parameter param, string newValue, InspectorParameterItem paramItem)
    {
        try
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(newValue);
                    return true;

                case StorageType.Integer:
                    if (paramItem.IsYesNo)
                    {
                        // Accept "Yes"/"No", "1"/"0", "True"/"False"
                        string lower = newValue.Trim().ToLowerInvariant();
                        if (lower is "yes" or "1" or "true")
                            param.Set(1);
                        else if (lower is "no" or "0" or "false")
                            param.Set(0);
                        else
                            return false;
                        return true;
                    }
                    if (int.TryParse(newValue.Trim(), out int intVal))
                    {
                        param.Set(intVal);
                        return true;
                    }
                    return false;

                case StorageType.Double:
                    if (double.TryParse(newValue.Trim(), out double dblVal))
                    {
                        // Convert from display units to internal units
                        try
                        {
                            var specTypeId = param.Definition.GetDataType();
                            var doc = param.Element.Document;
                            var formatOptions = doc.GetUnits().GetFormatOptions(specTypeId);
                            var unitTypeId = formatOptions.GetUnitTypeId();
                            double internalVal = UnitUtils.ConvertToInternalUnits(dblVal, unitTypeId);
                            param.Set(internalVal);
                        }
                        catch
                        {
                            // If unit conversion fails, try setting raw value
                            param.Set(dblVal);
                        }
                        return true;
                    }
                    return false;

                case StorageType.ElementId:
                    // V1: ElementId parameters are read-only
                    return false;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds a parameter on an element by its Definition.Name.
    /// </summary>
    private static Parameter? FindParameter(Element elem, string definitionName)
    {
        foreach (Parameter p in elem.Parameters)
        {
            if (p.Definition?.Name == definitionName)
                return p;
        }
        return null;
    }
}
