namespace BurhanScreenCapture.Services;

/// <summary>Tiny file logger (one file per day, kept 14 days). Never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex is null ? msg : $"{msg}: {ex}");

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var file = Path.Combine(AppPaths.Logs, $"log-{DateTime.Now:yyyyMMdd}.txt");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void Cleanup()
    {
        try
        {
            foreach (var f in Directory.GetFiles(AppPaths.Logs, "log-*.txt"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)) File.Delete(f);
        }
        catch { }
    }
}
