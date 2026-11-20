using Autodesk.Windows;

namespace w_finder.Services;

/// <summary>
/// Invokes Revit ribbon commands that aren't accessible via UIApplication.PostCommand
/// (the internal system commands like ID_MODIFY_CROP_AREA). Walks the live ribbon
/// tree (Autodesk.Windows.ComponentManager.Ribbon), finds a RibbonCommandItem with a
/// matching Id, and calls its CommandHandler directly — the same ICommand the button
/// fires when a user clicks it.
///
/// Only finds items currently present in the ribbon. Contextual tabs (e.g. "Modify |
/// Viewports") are only in the tree when their context is active — which is exactly
/// when we want their commands anyway.
///
/// Must run on the Revit UI thread. Call from inside RevitBackgroundTask.Raise.
/// </summary>
public static class RibbonCommandInvoker
{
    /// <summary>
    /// Tries to find and execute the ribbon item with the given command ID.
    /// Returns true on success. diagnostic is filled with a one-token reason
    /// describing the outcome (for logging).
    /// </summary>
    public static bool TryExecute(string commandId, out string diagnostic)
    {
        diagnostic = "init";
        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                diagnostic = "ribbon-null";
                return false;
            }

            RibbonCommandItem? match = null;
            foreach (var tab in ribbon.Tabs)
            {
                if (tab == null) continue;
                foreach (var panel in tab.Panels)
                {
                    if (panel?.Source == null) continue;
                    foreach (var item in panel.Source.Items)
                    {
                        match = FindMatch(item, commandId);
                        if (match != null) break;
                    }
                    if (match != null) break;
                }
                if (match != null) break;
            }

            if (match == null)
            {
                diagnostic = "no-match";
                return false;
            }

            if (!match.IsEnabled)
            {
                diagnostic = "disabled";
                return false;
            }

            var handler = match.CommandHandler;
            if (handler == null)
            {
                diagnostic = "no-handler";
                return false;
            }

            var param = match.CommandParameter ?? match;
            if (!handler.CanExecute(param))
            {
                diagnostic = "canexec-false";
                return false;
            }

            handler.Execute(param);
            diagnostic = "executed";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"ex-{ex.GetType().Name}:{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Recursively searches a ribbon item (and any sub-items it contains) for a
    /// RibbonCommandItem whose Id matches. Handles the container subclasses we
    /// know about — others fall through and just don't match.
    /// </summary>
    private static RibbonCommandItem? FindMatch(RibbonItem? item, string commandId)
    {
        if (item == null) return null;

        if (item is RibbonCommandItem ci &&
            string.Equals(ci.Id, commandId, StringComparison.OrdinalIgnoreCase))
        {
            return ci;
        }

        // SplitButton / ListButton hold a flat list of children — usually RibbonCommandItem,
        // but the collection is typed as RibbonItem so we pattern-match defensively.
        if (item is RibbonListButton list)
        {
            foreach (var sub in list.Items)
            {
                if (sub is RibbonCommandItem subCmd &&
                    string.Equals(subCmd.Id, commandId, StringComparison.OrdinalIgnoreCase))
                {
                    return subCmd;
                }
            }
        }

        // RowPanel / FoldPanel nest arbitrary RibbonItems — recurse.
        if (item is RibbonRowPanel row)
        {
            foreach (var sub in row.Items)
            {
                var hit = FindMatch(sub, commandId);
                if (hit != null) return hit;
            }
        }

        if (item is RibbonFoldPanel fold)
        {
            foreach (var sub in fold.Items)
            {
                var hit = FindMatch(sub, commandId);
                if (hit != null) return hit;
            }
        }

        return null;
    }
}
