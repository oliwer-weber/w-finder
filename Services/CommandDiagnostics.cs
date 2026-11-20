using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.UI;

namespace w_finder.Services;

/// <summary>
/// Appends per-attempt info to %AppData%\Quip\command-diagnostics.log so we can
/// figure out why non-postable commands silently fail. Compare a known-working
/// PostableCommand entry to a broken Lookup entry — the diff tells us where
/// it dies (null lookup vs. CanPostCommand=false vs. PostCommand throwing).
/// </summary>
public static class CommandDiagnostics
{
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Quip", "command-diagnostics.log");

    private static readonly object _lock = new();

    public static void LogAttempt(
        string source,
        string requestedId,
        RevitCommandId? resolved,
        bool canPost,
        UIApplication uiApp,
        Exception? postException,
        string? ribbonResult = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("] ");
            sb.Append("source=").Append(source);
            sb.Append(" | requested=").Append(requestedId);
            sb.Append(" | lookup=").Append(resolved == null ? "NULL" : "ok");
            if (resolved != null)
            {
                sb.Append(" | resolvedName=").Append(Safe(() => resolved.Name));
            }
            sb.Append(" | canPost=").Append(canPost);
            if (ribbonResult != null)
            {
                sb.Append(" | ribbon=").Append(ribbonResult);
            }

            try
            {
                var uidoc = uiApp.ActiveUIDocument;
                if (uidoc == null)
                {
                    sb.Append(" | activeDoc=NULL");
                }
                else
                {
                    var view = uidoc.ActiveView;
                    sb.Append(" | viewType=").Append(view?.ViewType.ToString() ?? "null");
                    sb.Append(" | viewName=\"").Append(view?.Name ?? "null").Append('"');
                    var sel = uidoc.Selection?.GetElementIds();
                    sb.Append(" | selCount=").Append(sel == null ? "?" : sel.Count.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ctxEx)
            {
                sb.Append(" | ctxErr=").Append(ctxEx.GetType().Name);
            }

            if (postException != null)
            {
                sb.Append(" | postEx=").Append(postException.GetType().Name)
                  .Append(": ").Append(postException.Message);
            }
            else if (resolved != null && canPost)
            {
                sb.Append(" | post=invoked");
            }
            else
            {
                sb.Append(" | post=skipped");
            }

            sb.AppendLine();

            lock (_lock)
            {
                File.AppendAllText(LogPath, sb.ToString());
            }
        }
        catch
        {
            // Diagnostics must never crash the plugin.
        }
    }

    private static string Safe(Func<string> getter)
    {
        try { return getter() ?? "null"; } catch { return "ERR"; }
    }
}
