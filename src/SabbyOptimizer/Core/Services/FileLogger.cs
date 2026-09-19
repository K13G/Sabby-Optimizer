using System.Collections.Concurrent;
using System.Text;

namespace PCTweaker.Core.Services;

public sealed class FileLogger : IAppLogger
{
    private readonly IAppPaths _paths;
    private readonly object _fileGate = new();
    private readonly ConcurrentQueue<string> _pending = new();
    private int _flushScheduled;

    public FileLogger(IAppPaths paths)
    {
        _paths = paths;

        // Log retention is maintenance work, not startup work.
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            ((FileLogger)state!).PruneOldLogs();
        }, this);
    }

    public void Info(string message) => Enqueue("INF", message, null);
    public void Warning(string message) => Enqueue("WRN", message, null);
    public void Error(string message, Exception? exception = null) => Enqueue("ERR", message, exception);
    public void Critical(string message, Exception? exception = null) => Enqueue("CRT", message, exception);

    private void Enqueue(string level, string message, Exception? exception)
    {
        try
        {
            var line = new StringBuilder(256)
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [").Append(level).Append("] ")
                .Append(message.Replace("\r", " ").Replace("\n", " "));

            if (exception is not null)
                line.AppendLine().Append(exception);

            line.AppendLine();
            _pending.Enqueue(line.ToString());
            ScheduleFlush();
        }
        catch
        {
            // Logging must never crash or delay the application.
        }
    }

    private void ScheduleFlush()
    {
        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0)
            return;

        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            ((FileLogger)state!).FlushPending();
        }, this);
    }

    private void FlushPending()
    {
        try
        {
            while (true)
            {
                var batch = new StringBuilder(4096);
                var count = 0;
                while (count < 128 && _pending.TryDequeue(out var line))
                {
                    batch.Append(line);
                    count++;
                }

                if (batch.Length > 0)
                {
                    Directory.CreateDirectory(_paths.LogsDirectory);
                    var file = Path.Combine(_paths.LogsDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
                    lock (_fileGate)
                        File.AppendAllText(file, batch.ToString(), Encoding.UTF8);
                }

                if (_pending.IsEmpty)
                    break;
            }
        }
        catch
        {
            // Logging is best-effort.
        }
        finally
        {
            Interlocked.Exchange(ref _flushScheduled, 0);
            if (!_pending.IsEmpty)
                ScheduleFlush();
        }
    }

    private void PruneOldLogs()
    {
        try
        {
            Directory.CreateDirectory(_paths.LogsDirectory);
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
