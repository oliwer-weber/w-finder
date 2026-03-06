using Autodesk.Revit.UI;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using w_finder.Helpers;
using w_finder.ViewModels;

namespace w_finder;

/// <summary>
/// Entry point — registers the ribbon button and dockable pane on Revit startup.
/// </summary>
public class App : IExternalApplication
{
    // This GUID must match what we use when toggling the pane in FinderCommand.
    public static readonly DockablePaneId PaneId = new(new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"));

    // Shared ViewModel — the dockable pane's WPF view binds to this.
    public static FinderPaneViewModel ViewModel { get; } = new();

    public Result OnStartup(UIControlledApplication application)
    {
        // Initialize the ExternalEvent handler so WPF can call Revit API safely
        RevitBackgroundTask.Initialize();

        // Register the dockable pane (must happen during OnStartup)
        var paneProvider = new FinderDockablePane();
        application.RegisterDockablePane(PaneId, "w_finder", paneProvider);

        // Create a ribbon tab and button to toggle the pane
        CreateRibbonButton(application);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }

    private void CreateRibbonButton(UIControlledApplication application)
    {
        string tabName = "w_finder";
        application.CreateRibbonTab(tabName);

        var panel = application.CreateRibbonPanel(tabName, "Tools");

        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        var buttonData = new PushButtonData(
            name: "ToggleFinder",
            text: "w_finder",
            assemblyName: assemblyPath,
            className: "w_finder.FinderCommand");

        var button = panel.AddItem(buttonData) as PushButton;
        if (button != null)
        {
            button.LargeImage = CreateMagnifyingGlassIcon(32);
            button.Image = CreateMagnifyingGlassIcon(16);
            button.ToolTip = "Toggle the w_finder search pane";
        }
    }

    /// <summary>
    /// Draws a simple magnifying glass icon at the given size using WPF drawing.
    /// No external image files needed.
    /// </summary>
    private static BitmapSource CreateMagnifyingGlassIcon(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double s = size;
            double strokeWidth = s < 20 ? 1.5 : 2.0;

            // Circle (lens) — centered in the upper-left area
            double cx = s * 0.4;
            double cy = s * 0.38;
            double radius = s * 0.28;

            var pen = new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), strokeWidth);
            pen.Freeze();

            dc.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(30, 100, 160, 220)),
                pen,
                new Point(cx, cy),
                radius, radius);

            // Handle — line from bottom-right of circle toward corner
            double angle = Math.PI / 4; // 45 degrees
            double handleStartX = cx + radius * Math.Cos(angle);
            double handleStartY = cy + radius * Math.Sin(angle);
            double handleEndX = s * 0.82;
            double handleEndY = s * 0.80;

            var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), strokeWidth * 1.5)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            handlePen.Freeze();

            dc.DrawLine(handlePen,
                new Point(handleStartX, handleStartY),
                new Point(handleEndX, handleEndY));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
