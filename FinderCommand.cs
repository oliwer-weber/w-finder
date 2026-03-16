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
            // Save current state before hiding (for "remember" launch behaviors)
            SettingsService.SaveLastState((int)App.ViewModel.ActiveMode, App.ViewModel.SearchText);
            pane.Hide();
        }
        else
        {
            // Refresh browser items before showing the pane
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc != null)
            {
                // Initialize recent items store for this project
                var projectKey = doc.IsModelInCloud
                    ? doc.GetCloudModelPath().GetModelGUID().ToString()
                    : doc.PathName;
                if (!string.IsNullOrEmpty(projectKey))
                    RecentItemsStore.SetProject(projectKey);

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
            pane.Show();

            // Apply launch behavior
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
        }
    }
}
