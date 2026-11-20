using w_finder.Helpers;
using w_finder.Services;
using w_finder.ViewModels;

namespace w_finder.Views;

// !pf, Parameter Filter: a four-field form (scope, parameter, rule, value)
// that selects every element matching the rule.
//
// The search box types into whichever field has focus and the list below
// offers suggestions for it. Enter picks, Tab moves between fields,
// Ctrl+Enter runs the filter from anywhere.
public partial class FinderPaneView
{
    /// <summary>
    /// Builds the parameter-name index on the Revit thread and opens the form.
    /// Shows a transient "Indexing..." hint so the UI feels responsive.
    /// </summary>
    private void LaunchParameterFilter()
    {
        App.ViewModel.ShowInlineSuccess("Indexing parameters…");

        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null)
            {
                ShowError("Open a project first");
                return;
            }

            // The form opens on "Entire Project", so index against that.
            var names = ParameterFilterService.CollectParameterNames(
                uidoc.Document, ParamFilterScope.EntireProject, uidoc.ActiveView);

            Dispatcher.Invoke(() =>
            {
                App.ViewModel.ClearInlineError();
                App.ViewModel.EnterParameterFilterMode(names);
            });
        });
    }

    // ── Keys (called from the shared keyboard handler) ─────────────

    /// <summary>
    /// Enter / double-click: pick the highlighted suggestion (or the typed
    /// text) for the focused field and move on. On the last field it runs
    /// the filter.
    /// </summary>
    private void ParamFilterActivateHighlighted()
    {
        var vm = App.ViewModel;

        if (vm.ParamFilterFocus == ParamFilterField.Value)
        {
            ParamFilterCommitValue();

            if (vm.ParamFilterCanExecute)
                vm.RequestParamFilterExecute();
            return;
        }

        bool committed = vm.ParamFilterCommitHighlight();

        // Committing moves focus to the next field. If that wrapped round to
        // Scope, the chosen rule needs no value ("has a value", "is empty"),
        // so there is nothing left to fill in: run it.
        if (committed && vm.ParamFilterFocus == ParamFilterField.Scope && vm.ParamFilterCanExecute)
            vm.RequestParamFilterExecute();
    }

    /// <summary>
    /// Stores the Value field. A suggestion the user arrowed to or clicked
    /// wins; otherwise what they typed; otherwise the first suggestion.
    /// </summary>
    private void ParamFilterCommitValue()
    {
        var vm = App.ViewModel;
        bool typedSomething = !string.IsNullOrWhiteSpace(vm.SearchText);

        if (_cursorMovedSinceTyping && vm.HighlightedItem != null)
            vm.ParamFilterCommitHighlight();
        else if (typedSomething)
            vm.ParamFilterCommitTyped();
        else
            vm.ParamFilterCommitHighlight();
    }

    /// <summary>Tab / Shift+Tab: next / previous field.</summary>
    private void ParamFilterTab(int direction)
    {
        var vm = App.ViewModel;

        // Keep what was typed or picked in Value before leaving it.
        if (vm.ParamFilterFocus == ParamFilterField.Value
            && (_cursorMovedSinceTyping || !string.IsNullOrWhiteSpace(vm.SearchText)))
            ParamFilterCommitValue();

        vm.ParamFilterMoveFocus(direction);
    }

    /// <summary>Ctrl+Enter: run the filter from whichever field has focus.</summary>
    private void ParamFilterRun()
    {
        var vm = App.ViewModel;

        if (vm.ParamFilterFocus == ParamFilterField.Value
            && (_cursorMovedSinceTyping || !string.IsNullOrWhiteSpace(vm.SearchText)))
            ParamFilterCommitValue();

        if (vm.ParamFilterCanExecute)
            vm.RequestParamFilterExecute();
        else
            vm.ShowInlineError("Pick a parameter and a rule first");
    }

    // ── Revit work ─────────────────────────────────────────────────

    /// <summary>
    /// Called when a parameter name is picked. Loads the value suggestions
    /// for that parameter on the Revit thread.
    /// </summary>
    private void OnParamFilterParamSelected(string parameterName)
    {
        var scope = App.ViewModel.ParamFilterScopeValue;

        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            var values = ParameterFilterService.CollectValuesFor(
                uidoc.Document, parameterName, scope, uidoc.ActiveView);

            Dispatcher.Invoke(() => App.ViewModel.SetParamFilterValueSuggestions(values));
        });
    }

    /// <summary>
    /// Runs the filter. With matches: they become the Revit selection and the
    /// pane closes. Without: the form stays as it is, so the rule or value
    /// can be adjusted and tried again.
    /// </summary>
    private void OnParamFilterExecuteRequested()
    {
        var vm = App.ViewModel;
        if (!vm.ParamFilterCanExecute) return;

        var scope = vm.ParamFilterScopeValue;
        var paramName = vm.ParamFilterParameterName!;
        var rule = vm.ParamFilterRuleValue!.Value;
        var value = vm.ParamFilterValueText;

        RevitBackgroundTask.Raise(uiApp =>
        {
            var uidoc = uiApp.ActiveUIDocument;
            if (uidoc == null) return;

            var matches = ParameterFilterService.Execute(
                uidoc.Document, scope, uidoc.ActiveView, paramName, rule, value);

            if (matches.Count == 0)
            {
                ShowError("No elements matched. Adjust the filter and try again");
                return;
            }

            uidoc.Selection.SetElementIds(matches);
            FinderCommand.ClosePane(uiApp);
        });
    }
}
