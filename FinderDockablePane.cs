using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using w_finder.Helpers;
using w_finder.Views;

// Revit.UI also defines a TextBox; here we always mean the WPF control.
using TextBox = System.Windows.Controls.TextBox;

namespace w_finder;

/// <summary>
/// Provides the WPF content for the dockable pane.
/// Revit calls SetupDockablePane once, lazily, when it first builds the pane.
/// </summary>
public class FinderDockablePane : IDockablePaneProvider
{
    public void SetupDockablePane(DockablePaneProviderData data)
    {
        // If building the view throws, the exception escapes into Revit's own
        // startup and takes the whole application down with an "unrecoverable
        // error" dialog that names nothing. Catching it here costs the pane but
        // keeps Revit alive, and writes down what actually went wrong.
        QuipLog.Write("SetupDockablePane: entered");

        FrameworkElement content;
        try
        {
            // Constructed via a separate non-inlined method on purpose. If the
            // failure is a type-load or JIT failure rather than something thrown
            // by the constructor body, it surfaces when the *containing method*
            // is entered. Calling `new FinderPaneView()` directly here would
            // therefore blow up as this method starts, before the try block is
            // live, and escape the catch entirely. Isolating it means the
            // failure happens on the call, inside the try, where we can see it.
            content = CreateView();
            QuipLog.Write("SetupDockablePane: view constructed OK");
        }
        catch (Exception ex)
        {
            QuipLog.WriteException("SetupDockablePane", ex);
            content = BuildFailureView(ex);
            TryWriteCrashLog(ex);
        }

        data.FrameworkElement = content;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Floating
        };
        // Place the floating pane at a visible on-screen position (left, top, right, bottom)
        data.InitialState.SetFloatingRectangle(new Autodesk.Revit.DB.Rectangle(100, 100, 500, 700));
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static FrameworkElement CreateView() => new FinderPaneView();

    /// <summary>
    /// A plain, dependency-free panel shown in place of the real pane so the
    /// error is visible rather than fatal. Deliberately uses no themes, styles
    /// or converters, since any of those could be what failed.
    /// </summary>
    private static FrameworkElement BuildFailureView(Exception ex)
    {
        var text = new TextBox
        {
            Text = "Quip failed to load its pane.\r\n\r\n" + Describe(ex),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(10),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11,
        };

        return new Grid { Children = { text } };
    }

    /// <summary>Flattens the exception chain; the inner one is usually the real cause.</summary>
    private static string Describe(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var e = (Exception?)ex;
        int depth = 0;

        while (e != null)
        {
            sb.AppendLine($"[{depth}] {e.GetType().FullName}");
            sb.AppendLine(e.Message);
            if (!string.IsNullOrWhiteSpace(e.StackTrace))
            {
                sb.AppendLine(e.StackTrace);
            }
            sb.AppendLine();
            e = e.InnerException;
            depth++;
        }

        return sb.ToString();
    }

    private static void TryWriteCrashLog(Exception ex)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"Quip_pane_error_{DateTime.Now:yyyy-MM-dd_HHmmss}.txt");

            System.IO.File.WriteAllText(path, Describe(ex));
        }
        catch { /* nothing useful left to do */ }
    }
}
