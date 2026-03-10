using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.IO;

namespace w_finder;

/// <summary>
/// Emergency reset: generates a new pane GUID so Revit forgets the old
/// (possibly off-screen) position on next restart.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ResetPaneCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            // Generate a brand-new GUID and persist it
            var newGuid = Guid.NewGuid();
            string dir = Path.GetDirectoryName(App.PaneGuidFilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(App.PaneGuidFilePath, newGuid.ToString());

            // Also try the normal hide/show in case it helps right now
            var uiApp = commandData.Application;
            var pane = uiApp.GetDockablePane(App.PaneId);
            pane.Hide();
            pane.Show();

            TaskDialog.Show("Rauncher",
                "Pane reset scheduled!\n\n" +
                "A new pane ID has been saved. Restart Revit and the pane " +
                "will reappear as a floating window near the top-left of your screen.\n\n" +
                "(The hide/show trick was also attempted — check if the pane is visible now.)");
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Rauncher Error", ex.ToString());
        }

        return Result.Succeeded;
    }
}
