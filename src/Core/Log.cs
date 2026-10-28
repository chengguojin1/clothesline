using System.IO;
using System.Text;

namespace Clothesline.Core;

/// <summary>Tiny append-only log in %LOCALAPPDATA%\Clothesline\logs. Keeps the last 7 days.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static bool _pruned;

    public static void Info(string message) => Write("INFO ", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                if (!_pruned) Prune();
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ').Append(level).Append(' ').Append(message);
                if (ex != null) sb.AppendLine().Append(ex);
                sb.AppendLine();
                File.AppendAllText(Path.Combine(AppPaths.Logs, $"clothesline-{DateTime.Now:yyyyMMdd}.log"), sb.ToString());
            }
        }
        catch { /* logging must never take the app down */ }
    }

    private static void Prune()
    {
        _pruned = true;
        foreach (var f in Directory.EnumerateFiles(AppPaths.Logs, "clothesline-*.log"))
            if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7))
                try { File.Delete(f); } catch { }
    }
}
