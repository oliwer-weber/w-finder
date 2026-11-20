using System.IO;
using System.Text;

namespace w_finder.Helpers;

/// <summary>
/// Minimal breadcrumb log for startup problems.
/// Revit's "unrecoverable error" dialog names nothing, and its journal only
/// records that a managed exception happened, so we need our own trail.
/// Lives next to pane_guid.txt rather than in Documents, to stay out of the way.
/// </summary>
public static class QuipLog
{
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Quip", "startup.log");

    /// <summary>Truncates the log so each Revit session starts clean.</summary>
    public static void StartSession()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, $"=== Quip session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch { /* logging must never be the thing that breaks startup */ }
    }

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void WriteException(string where, Exception ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{DateTime.Now:HH:mm:ss.fff}  EXCEPTION in {where}");

            var e = (Exception?)ex;
            int depth = 0;
            while (e != null)
            {
                sb.AppendLine($"  [{depth}] {e.GetType().FullName}: {e.Message}");
                if (!string.IsNullOrWhiteSpace(e.StackTrace))
                    sb.AppendLine(e.StackTrace);
                e = e.InnerException;
                depth++;
            }

            File.AppendAllText(FilePath, sb.ToString());
        }
        catch { }
    }
}
