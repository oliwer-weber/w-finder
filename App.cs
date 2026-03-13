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
        "Quip", "pane_guid.txt");

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
        application.RegisterDockablePane(PaneId, "Quip", paneProvider);

        // Create a ribbon tab and button to toggle the pane
        CreateRibbonButton(application);

        // Install a low-level keyboard hook so the hotkey works even when
        // a schedule view has captured keyboard input.
        GlobalKeyboardHook.Install(() =>
        {
            RevitBackgroundTask.Raise(FinderCommand.TogglePane);
        });

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        GlobalKeyboardHook.Uninstall();
        return Result.Succeeded;
    }

    private void CreateRibbonButton(UIControlledApplication application)
    {
        string tabName = "Quip";
        application.CreateRibbonTab(tabName);

        var panel = application.CreateRibbonPanel(tabName, "Tools");

        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        var buttonData = new PushButtonData(
            name: "ToggleFinder",
            text: "Quip",
            assemblyName: assemblyPath,
            className: "w_finder.FinderCommand");

        bool isDark = IsRevitDarkTheme();

        var button = panel.AddItem(buttonData) as PushButton;
        if (button != null)
        {
            // Quip icon has its own background, so invert: dark icon on light Revit
            button.LargeImage = CreateQuipIcon(32, !isDark);
            button.Image = CreateQuipIcon(16, !isDark);
            button.ToolTip = "Toggle the Quip search pane";
        }

        var settingsData = new PushButtonData(
            name: "QuipSettings",
            text: "Settings",
            assemblyName: assemblyPath,
            className: "w_finder.SettingsCommand");

        var settingsButton = panel.AddItem(settingsData) as PushButton;
        if (settingsButton != null)
        {
            // Transparent background — white strokes on dark Revit, dark strokes on light
            settingsButton.LargeImage = CreateSettingsIcon(32, isDark);
            settingsButton.Image = CreateSettingsIcon(16, isDark);
            settingsButton.ToolTip = "Open Quip settings";
        }

        var resetData = new PushButtonData(
            name: "ResetPane",
            text: "Emergency\nReset",
            assemblyName: assemblyPath,
            className: "w_finder.ResetPaneCommand");

        var resetButton = panel.AddItem(resetData) as PushButton;
        if (resetButton != null)
        {
            // Transparent background — white strokes on dark Revit, dark strokes on light
            resetButton.LargeImage = CreateWarningIcon(32, isDark);
            resetButton.Image = CreateWarningIcon(16, isDark);
            resetButton.ToolTip = "Reset the Quip pane position if it's lost";
        }
    }

    /// <summary>
    /// Renders a warning icon (two offset triangles) at the given size.
    /// Based on 40x40 SVG viewBox, no background.
    /// </summary>
    private static BitmapSource CreateWarningIcon(int size, bool useDarkVariant)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double s = size;
            double scale = s / 40.0;

            var strokeColor = useDarkVariant ? Colors.White : Color.FromRgb(0x1a, 0x1a, 0x1a);
            double fadedOpacity = useDarkVariant ? 0.28 : 0.22;
            double strokeWidth = 4.5 * scale;

            // Faded triangle: M17,4 L33,33 L1,33 Z
            var fadedColor = strokeColor;
            fadedColor.A = (byte)(255 * fadedOpacity);
            var fadedPen = new Pen(new SolidColorBrush(fadedColor), strokeWidth)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            fadedPen.Freeze();

            var tri1 = new StreamGeometry();
            using (var ctx = tri1.Open())
            {
                ctx.BeginFigure(new Point(17 * scale, 4 * scale), false, true);
                ctx.LineTo(new Point(33 * scale, 33 * scale), true, false);
                ctx.LineTo(new Point(1 * scale, 33 * scale), true, false);
            }
            tri1.Freeze();
            dc.DrawGeometry(null, fadedPen, tri1);

            // Solid triangle: M23,8 L39,37 L7,37 Z
            var solidPen = new Pen(new SolidColorBrush(strokeColor), strokeWidth)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            solidPen.Freeze();

            var tri2 = new StreamGeometry();
            using (var ctx = tri2.Open())
            {
                ctx.BeginFigure(new Point(23 * scale, 8 * scale), false, true);
                ctx.LineTo(new Point(39 * scale, 37 * scale), true, false);
                ctx.LineTo(new Point(7 * scale, 37 * scale), true, false);
            }
            tri2.Freeze();
            dc.DrawGeometry(null, solidPen, tri2);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Renders a settings icon (two slider lines with knobs) at the given size.
    /// Based on 40x40 SVG viewBox, no background.
    /// </summary>
    private static BitmapSource CreateSettingsIcon(int size, bool useDarkVariant)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double s = size;
            double scale = s / 40.0;

            var strokeColor = useDarkVariant ? Colors.White : Color.FromRgb(0x1a, 0x1a, 0x1a);
            var fillColor = useDarkVariant
                ? Color.FromRgb(0x14, 0x14, 0x14)
                : Color.FromRgb(0xf0, 0xee, 0xea);
            double fadedOpacity = useDarkVariant ? 0.28 : 0.22;
            double strokeWidth = 4.5 * scale;

            // Faded horizontal lines
            var fadedColor = strokeColor;
            fadedColor.A = (byte)(255 * fadedOpacity);
            var linePen = new Pen(new SolidColorBrush(fadedColor), strokeWidth)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            linePen.Freeze();

            // Line at y=13
            dc.DrawLine(linePen,
                new Point(6 * scale, 13 * scale),
                new Point(34 * scale, 13 * scale));
            // Line at y=27
            dc.DrawLine(linePen,
                new Point(6 * scale, 27 * scale),
                new Point(34 * scale, 27 * scale));

            // Slider knob circles (solid stroke, filled with background color)
            var knobPen = new Pen(new SolidColorBrush(strokeColor), strokeWidth);
            knobPen.Freeze();
            var knobFill = new SolidColorBrush(fillColor);
            knobFill.Freeze();
            double r = 4.5 * scale;

            // Knob at (24, 13)
            dc.DrawEllipse(knobFill, knobPen,
                new Point(24 * scale, 13 * scale), r, r);
            // Knob at (16, 27)
            dc.DrawEllipse(knobFill, knobPen,
                new Point(16 * scale, 27 * scale), r, r);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Checks whether Revit is currently using a dark theme.
    /// Reads "Theme=0" (dark) or "Theme=1" (light) from Revit.ini under [Colors].
    /// </summary>
    private static bool IsRevitDarkTheme()
    {
        try
        {
            string iniPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "Revit", "Autodesk Revit 2025", "Revit.ini");

            if (File.Exists(iniPath))
            {
                foreach (var line in File.ReadAllLines(iniPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Theme=", StringComparison.OrdinalIgnoreCase))
                    {
                        // Theme=0 → dark, Theme=1 → light
                        return trimmed == "Theme=0";
                    }
                }
            }
        }
        catch { /* fall through */ }

        return false; // default to light theme assumption
    }

    /// <summary>
    /// Renders the Quip chevron icon (>>) at the given size.
    /// When useDarkVariant is true: dark background (#141414) with white strokes.
    /// When false: light background (#f0eeea) with dark strokes (#1a1a1a).
    /// </summary>
    private static BitmapSource CreateQuipIcon(int size, bool useDarkVariant)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double s = size;

            // Rounded-rect background
            var bgColor = useDarkVariant
                ? Color.FromRgb(0x14, 0x14, 0x14)
                : Color.FromRgb(0xf0, 0xee, 0xea);
            var bgBrush = new SolidColorBrush(bgColor);
            bgBrush.Freeze();

            double cornerRadius = s * (22.0 / 96.0);
            dc.DrawRoundedRectangle(bgBrush, null,
                new Rect(0, 0, s, s), cornerRadius, cornerRadius);

            // Stroke color and opacity for the two chevrons
            var strokeColor = useDarkVariant ? Colors.White : Color.FromRgb(0x1a, 0x1a, 0x1a);
            double fadedOpacity = useDarkVariant ? 0.28 : 0.22;

            // Scale from 96x96 SVG coordinate space to target size
            // SVG transform: translate(20,20) scale(1.077)
            double scale = s / 96.0;
            double tx = 20.0 * scale;
            double ty = 20.0 * scale;
            double sc = 1.077 * scale;
            double strokeWidth = 4.5 * sc;

            // Faded first chevron: M6,10 L22,26 L6,42
            var fadedColor = strokeColor;
            fadedColor.A = (byte)(255 * fadedOpacity);
            var fadedPen = new Pen(new SolidColorBrush(fadedColor), strokeWidth)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            fadedPen.Freeze();

            var chevron1 = new StreamGeometry();
            using (var ctx = chevron1.Open())
            {
                ctx.BeginFigure(new Point(tx + 6 * sc, ty + 10 * sc), false, false);
                ctx.LineTo(new Point(tx + 22 * sc, ty + 26 * sc), true, false);
                ctx.LineTo(new Point(tx + 6 * sc, ty + 42 * sc), true, false);
            }
            chevron1.Freeze();
            dc.DrawGeometry(null, fadedPen, chevron1);

            // Solid second chevron: M22,10 L38,26 L22,42
            var solidPen = new Pen(new SolidColorBrush(strokeColor), strokeWidth)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            solidPen.Freeze();

            var chevron2 = new StreamGeometry();
            using (var ctx = chevron2.Open())
            {
                ctx.BeginFigure(new Point(tx + 22 * sc, ty + 10 * sc), false, false);
                ctx.LineTo(new Point(tx + 38 * sc, ty + 26 * sc), true, false);
                ctx.LineTo(new Point(tx + 22 * sc, ty + 42 * sc), true, false);
            }
            chevron2.Freeze();
            dc.DrawGeometry(null, solidPen, chevron2);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
