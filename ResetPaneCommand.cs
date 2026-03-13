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
            var dialog = new TaskDialog("Quip")
            {
                MainInstruction = "Pane lost?",
                MainContent = "Reset its position to default, then restart Revit. " +
                              "Your settings and data are untouched.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Reset position");

            var result = dialog.Show();
            if (result != TaskDialogResult.CommandLink1)
                return Result.Cancelled;

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
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Quip Error", ex.ToString());
        }

        return Result.Succeeded;
    }
}
