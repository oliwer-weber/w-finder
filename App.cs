using Autodesk.Revit.UI;
using System.IO;
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
    private static readonly Guid DefaultPaneGuid = new("b2c3d4e5-f6a7-8901-bcde-f12345678901");

    // Reads GUID from file if a reset was requested, otherwise uses the default.
    // Revit caches pane positions by GUID, so a new GUID = fresh floating position.
    public static readonly DockablePaneId PaneId = new(LoadPaneGuid());

    /// <summary>
    /// File where we persist the pane GUID. ResetPaneCommand writes a new GUID here.
    /// </summary>
    public static string PaneGuidFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Rauncher", "pane_guid.txt");

    private static Guid LoadPaneGuid()
    {
        try
        {
            if (File.Exists(PaneGuidFilePath))
            {
                string text = File.ReadAllText(PaneGuidFilePath).Trim();
                if (Guid.TryParse(text, out var guid))
                    return guid;
            }
        }
        catch { /* fall through to default */ }
        return DefaultPaneGuid;
    }

    // Shared ViewModel — the dockable pane's WPF view binds to this.
    public static FinderPaneViewModel ViewModel { get; } = new();

    public Result OnStartup(UIControlledApplication application)
    {
        // Initialize the ExternalEvent handler so WPF can call Revit API safely
        RevitBackgroundTask.Initialize();

        // Register the dockable pane (must happen during OnStartup)
        var paneProvider = new FinderDockablePane();
        application.RegisterDockablePane(PaneId, "Rauncher", paneProvider);

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
        string tabName = "Rauncher";
        application.CreateRibbonTab(tabName);

        var panel = application.CreateRibbonPanel(tabName, "Tools");

        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        var buttonData = new PushButtonData(
            name: "ToggleFinder",
            text: "Rauncher",
            assemblyName: assemblyPath,
            className: "w_finder.FinderCommand");

        var button = panel.AddItem(buttonData) as PushButton;
        if (button != null)
        {
            button.LargeImage = CreateMagnifyingGlassIcon(32);
            button.Image = CreateMagnifyingGlassIcon(16);
            button.ToolTip = "Toggle the Rauncher search pane";
        }

        var resetData = new PushButtonData(
            name: "ResetPane",
            text: "Reset\nPane",
            assemblyName: assemblyPath,
            className: "w_finder.ResetPaneCommand");

        var resetButton = panel.AddItem(resetData) as PushButton;
        if (resetButton != null)
        {
            resetButton.ToolTip = "Force-reset the Rauncher pane if it's invisible";
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
