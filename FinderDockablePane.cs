using Autodesk.Revit.UI;
using w_finder.Views;

namespace w_finder;

/// <summary>
/// Provides the WPF content for the dockable pane.
/// Revit calls SetupDockablePane once during registration.
/// </summary>
public class FinderDockablePane : IDockablePaneProvider
{
    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = new FinderPaneView();
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Floating
        };
        // Place the floating pane at a visible on-screen position (left, top, right, bottom)
        data.InitialState.SetFloatingRectangle(new Autodesk.Revit.DB.Rectangle(100, 100, 500, 700));
    }
}
