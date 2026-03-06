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
            DockPosition = DockPosition.Right
        };
    }
}
