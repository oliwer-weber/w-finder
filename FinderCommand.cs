using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using w_finder.Services;
using w_finder.ViewModels;

namespace w_finder;

/// <summary>
/// Toggles the Quip dockable pane visibility when the ribbon button is clicked.
/// Also refreshes the browser item cache each time the pane is shown.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class FinderCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TogglePane(commandData.Application);
        return Result.Succeeded;
    }

    /// <summary>
    /// Toggles the Quip pane visibility. Extracted as a static method so both
    /// the ribbon command and the global keyboard hook can call it.
    /// </summary>
    public static void TogglePane(UIApplication uiApp)
    {
        var pane = uiApp.GetDockablePane(App.PaneId);

        if (pane.IsShown())
        {
            ClosePane(uiApp);
        }
        else
        {
            // Defensive: if the pane was hidden by some other path (e.g. the
            // floating pane's close-X) while a quip, rename box or action bar
            // was open, clear all of that so it opens on a clean search.
            // refresh: false because the items are reloaded just below.
            App.ViewModel.ResetTransientState(refresh: false);

            // Refresh browser items before showing the pane
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc != null)
            {
                // Initialize recent items store for this project
                var projectKey = doc.IsModelInCloud
                    ? doc.GetCloudModelPath().GetModelGUID().ToString()
                    : doc.PathName;
                if (!string.IsNullOrEmpty(projectKey))
                    UsageStore.SetProject(projectKey);

                var items = BrowserItemCollector.Collect(doc);
                var favoriteIds = FavoritesStore.Load(doc);
                App.ViewModel.LoadItems(items, favoriteIds);
            }

            // Load command and shebang items (static, not per-document)
            App.ViewModel.LoadCommands(CommandCollector.Collect(uiApp));
            App.ViewModel.LoadShebangs(ShebangService.Collect());

            // Apply current settings
            var settings = SettingsService.Current;
            App.ViewModel.FilterPlacedTypes = settings.FilterPlacedTypesOnly;

            App.ViewModel.AreCategoryChipsExpanded = false;

            // Apply launch behavior BEFORE showing the pane so the UI renders
            // in the correct mode immediately — eliminates white flash on CMD mode, etc.
            switch ((LaunchBehavior)settings.LaunchBehavior)
            {
                case LaunchBehavior.RememberMode:
                    App.ViewModel.RestoreState((ActiveMode)settings.LastActiveMode, string.Empty);
                    break;
                case LaunchBehavior.RememberAll:
                    App.ViewModel.RestoreState((ActiveMode)settings.LastActiveMode, settings.LastSearchText);
                    break;
                default: // CleanSlate
                    App.ViewModel.ActivateDefaultMode((ActiveMode)settings.DefaultMode);
                    break;
            }

            pane.Show();
        }
    }

    /// <summary>
    /// The one and only way the pane closes. Every "we're done, hand control
    /// back to Revit" path calls this, so they all behave the same:
    /// remember the mode/search for next time, drop any open quip, rename box
    /// or action bar, then hide. Must run on the Revit API thread.
    /// </summary>
    public static void ClosePane(UIApplication uiApp)
    {
        var vm = App.ViewModel;

        // Reset first, then remember. Inside a quip or a family drill-down the
        // search box holds that screen's filter text, which means nothing next
        // time; after the reset it holds the real search (or nothing).
        vm.ResetTransientState();
        SettingsService.SaveLastState((int)vm.ActiveMode, vm.SearchText);

        var pane = uiApp.GetDockablePane(App.PaneId);
        if (pane.IsShown())
            pane.Hide();
    }
}
