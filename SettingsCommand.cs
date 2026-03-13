using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using w_finder.Views;

namespace w_finder;

/// <summary>
/// Opens the Quip Settings dialog.
/// Accessible from the ribbon panel and from command mode (: Quip Settings).
/// </summary>
[Transaction(TransactionMode.Manual)]
public class SettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var window = new SettingsWindow();
        var helper = new System.Windows.Interop.WindowInteropHelper(window);
        helper.Owner = commandData.Application.MainWindowHandle;
        window.ShowDialog();

        return Result.Succeeded;
    }
}
