using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using w_finder.Services;

namespace w_finder;

/// <summary>
/// Toggles the Rauncher dockable pane visibility when the ribbon button is clicked.
/// Also refreshes the browser item cache each time the pane is shown.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class FinderCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiApp = commandData.Application;
        var pane = uiApp.GetDockablePane(App.PaneId);

        if (pane.IsShown())
        {
            pane.Hide();
        }
        else
        {
            // Refresh browser items before showing the pane
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc != null)
            {
                var items = BrowserItemCollector.Collect(doc);
                var favoriteIds = FavoritesStore.Load(doc);
                App.ViewModel.LoadItems(items, favoriteIds);
            }

            // Load command items (static, not per-document)
            App.ViewModel.LoadCommands(CommandCollector.Collect());

            pane.Show();
            App.ViewModel.RequestFocusSearch();
        }

        return Result.Succeeded;
    }
}
