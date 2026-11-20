using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using w_finder.Helpers;
using w_finder.Models;
using w_finder.Services;

namespace w_finder.Views;

// !pi, Property Inspector: list and edit the parameters of the selected
// elements. Stays open while you work, since you usually edit several values.
public partial class FinderPaneView
{
    /// <summary>StorageType.ElementId as the plain number the view model stores.</summary>
    private const int ElementIdStorage = 4;

    /// <summary>
    /// Reads the current Revit selection and opens the inspector on it.
    /// </summary>
    private void LaunchPropertyInspector()
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null)
            {
                ShowError("Open a project first");
                return;
            }

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds.Count == 0)
            {
                ShowError("Select elements in the model first");
                return;
            }

            var (items, breadcrumb, isMixed) = ParameterCollector.Collect(uidoc.Document, selIds);
            var elementIdList = selIds.Select(id => id.Value).ToList();

            Dispatcher.Invoke(() =>
                App.ViewModel.EnterInspectorMode(items, breadcrumb, isMixed, elementIdList));
        });
    }

    // ── Keys (called from the shared keyboard handler) ─────────────

    /// <summary>Enter / double-click: open a category, or edit a parameter.</summary>
    private void InspectorActivateHighlighted()
    {
        var vm = App.ViewModel;
        var param = vm.GetInspectorParam(vm.HighlightedItem);
        if (param == null || param.IsDivider) return;

        if (param.IsCategoryRow)
        {
            InspectorDrillIntoCategory(param);
            return;
        }

        if (param.IsYesNo && !param.IsReadOnly)
        {
            // A yes/no has nothing to type: Enter flips it, same as Space.
            InspectorToggle(param);
            return;
        }

        if (param.IsReadOnly)
        {
            vm.ShowInlineError(param.StorageType == ElementIdStorage
                ? "This parameter points at another element and can't be edited here yet"
                : "Read-only parameter");
            return;
        }

        vm.EnterInspectorEdit(param);
    }

    /// <summary>Right arrow: open the highlighted category. False on any other row.</summary>
    private bool InspectorDrillHighlighted()
    {
        var vm = App.ViewModel;
        var param = vm.GetInspectorParam(vm.HighlightedItem);
        if (param is not { IsCategoryRow: true }) return false;

        InspectorDrillIntoCategory(param);
        return true;
    }

    /// <summary>Space: flip a yes/no parameter. False on any other row, so Space types.</summary>
    private bool InspectorToggleHighlighted()
    {
        var vm = App.ViewModel;
        var param = vm.GetInspectorParam(vm.HighlightedItem);
        if (param is not { IsYesNo: true, IsReadOnly: false }) return false;

        InspectorToggle(param);
        return true;
    }

    /// <summary>
    /// Divider rows are only labels, so the cursor steps over them.
    /// </summary>
    private void SkipInspectorDividers(int direction)
    {
        var vm = App.ViewModel;
        var param = vm.GetInspectorParam(vm.HighlightedItem);
        if (param is { IsDivider: true })
            vm.MoveHighlight(direction);
    }

    /// <summary>
    /// Keys inside the small edit box that appears over a parameter.
    /// </summary>
    private void InspectorEditBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = App.ViewModel;

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                vm.ConfirmInspectorEdit();
                break;

            case Key.Escape:
                e.Handled = true;
                vm.CancelInspectorEdit();
                break;

            case Key.Tab:
                e.Handled = true;
                int direction = Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1;
                vm.CommitAndMoveInspectorEdit(direction);
                break;
        }
    }

    // ── Revit work ─────────────────────────────────────────────────

    /// <summary>
    /// Opens one category from the mixed-selection picker: loads that
    /// category's parameters on the Revit thread, then shows them.
    /// </summary>
    private void InspectorDrillIntoCategory(InspectorParameterItem catItem)
    {
        if (catItem.CategoryName == null || catItem.ElementIds.Count == 0) return;

        App.ViewModel.InspectorDrillIn(catItem);

        var categoryName = catItem.CategoryName;
        var elementIds = catItem.ElementIds.ToList();
        int totalCount = App.ViewModel.InspectorElementCount;

        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var (items, breadcrumb) = ParameterCollector.CollectForCategory(
                doc, categoryName, elementIds, totalCount);

            Dispatcher.Invoke(() =>
            {
                App.ViewModel.InspectorShowCategoryParams(items, breadcrumb);
                UpdateResultGrouping();
            });
        });
    }

    /// <summary>
    /// Called when an inspector edit is confirmed: writes the value to Revit.
    /// </summary>
    private void OnInspectorEditConfirmed(InspectorParameterItem paramItem, string newValue)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var (success, message) = ParameterWriter.Write(doc, paramItem, newValue);

            if (success) ShowSuccess(message);
            else ShowError(message);

            if (success) RefreshInspectorParams(uiApp);
        });
    }

    private void InspectorToggle(InspectorParameterItem paramItem)
    {
        RevitBackgroundTask.Raise(uiApp =>
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null) return;

            var (success, message) = ParameterWriter.ToggleYesNo(doc, paramItem);

            if (success) ShowSuccess(message);
            else ShowError(message);

            if (success) RefreshInspectorParams(uiApp);
        });
    }

    /// <summary>
    /// Re-reads parameters from Revit and updates the inspector display,
    /// keeping the cursor on the same parameter.
    /// Must be called on the Revit API thread.
    /// </summary>
    private void RefreshInspectorParams(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc == null) return;

        var vm = App.ViewModel;
        var elementIds = vm.InspectorElementIds;
        if (elementIds.Count == 0) return;

        List<InspectorParameterItem> items;

        if (vm.InspectorDrilledCategory != null)
        {
            // ParameterCollector filters the ids down to the category itself.
            (items, _) = ParameterCollector.CollectForCategory(
                doc, vm.InspectorDrilledCategory, elementIds, vm.InspectorElementCount);
        }
        else
        {
            var revitIds = elementIds.Select(id => new ElementId(id)).ToList();
            (items, _, _) = ParameterCollector.Collect(doc, revitIds);
        }

        Dispatcher.Invoke(() => vm.RefreshInspectorParams(items));
    }
}
