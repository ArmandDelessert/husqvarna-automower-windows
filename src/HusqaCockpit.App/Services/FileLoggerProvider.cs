using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HusqaCockpit.App.Services;

/// <summary>Minimal daily-rolling file logger for troubleshooting; the files are kept for a chosen number of days.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _folder;
    private readonly LogLevel _minimumLevel;
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 10_000);
    private readonly Thread _writer;
    private int _retentionDays;

    public FileLoggerProvider(string folder, LogLevel minimumLevel, int retentionDays)
    {
        _folder = folder;
        _minimumLevel = minimumLevel;
        _retentionDays = retentionDays;
        Directory.CreateDirectory(folder);
        DeleteOldLogs();
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Log writer" };
        _writer.Start();
    }

    /// <summary>How many days a file is kept after its last write, 0 for ever. Setting it deletes the files now too old at once.</summary>
    public int RetentionDays
    {
        get => Volatile.Read(ref _retentionDays);
        set
        {
            Volatile.Write(ref _retentionDays, value);
            DeleteOldLogs();
        }
    }

    /// <summary>How many log files of <paramref name="folder"/> were last written more than <paramref name="days"/> days ago (none when 0, for ever).</summary>
    public static int CountOlderThan(string folder, int days) => OlderThan(folder, days).Count;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, ShortName(categoryName));

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
    }

    private static string ShortName(string category) => category[(category.LastIndexOf('.') + 1)..];

    private void Enqueue(string line) => _queue.TryAdd(line);

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                var path = Path.Combine(_folder, $"husqa-cockpit-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never crash the app.
            }
        }
    }

    private void DeleteOldLogs()
    {
        try
        {
            foreach (var file in OlderThan(_folder, RetentionDays))
            {
                File.Delete(file);
            }
        }
        catch (IOException)
        {
        }
    }

    private static List<string> OlderThan(string folder, int days)
    {
        if (days <= 0 || !Directory.Exists(folder))
        {
            return [];
        }
        var limit = TimeSpan.FromDays(days);
        return Directory.EnumerateFiles(folder, "*.log").Where(file => DateTime.Now - File.GetLastWriteTime(file) > limit).ToList();
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var builder = new StringBuilder()
                .Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(' ').Append(logLevel.ToString()[..4].ToUpperInvariant())
                .Append(' ').Append(category).Append(": ")
                .Append(formatter(state, exception))
                .AppendLine();
            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }
            owner.Enqueue(builder.ToString());
        }
    }
}
