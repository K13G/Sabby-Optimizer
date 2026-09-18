using System.IO;
using System.Text;

namespace PCTweaker.Core.Services;

public sealed class FileLogger : IAppLogger
{
    private readonly IAppPaths _paths;
    private readonly object _sync = new();

    public FileLogger(IAppPaths paths)
    {
        _paths = paths;
        PruneOldLogs();
    }

    public void Info(string message) => Write("INF", message, null);
    public void Warning(string message) => Write("WRN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERR", message, exception);
    public void Critical(string message, Exception? exception = null) => Write("CRT", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var file = Path.Combine(_paths.LogsDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [").Append(level).Append("] ")
                .Append(message.Replace("\r", " ").Replace("\n", " "));

            if (exception is not null)
                line.AppendLine().Append(exception);

            line.AppendLine();

            lock (_sync)
                File.AppendAllText(file, line.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Logging must never crash the application.
        }
    }

    private void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-14);
            foreach (var file in Directory.EnumerateFiles(_paths.LogsDirectory, "app-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // Retention cleanup is best-effort only.
        }
    }
}
