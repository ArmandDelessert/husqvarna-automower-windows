using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HusqaCockpit.App.Services;

/// <summary>Minimal daily-rolling file logger (keeps one week of logs) for troubleshooting.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly TimeSpan s_retention = TimeSpan.FromDays(7);

    private readonly string _folder;
    private readonly LogLevel _minimumLevel;
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 10_000);
    private readonly Thread _writer;

    public FileLoggerProvider(string folder, LogLevel minimumLevel)
    {
        _folder = folder;
        _minimumLevel = minimumLevel;
        Directory.CreateDirectory(folder);
        DeleteOldLogs();
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Log writer" };
        _writer.Start();
    }

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
            foreach (var file in Directory.EnumerateFiles(_folder, "*.log"))
            {
                if (DateTime.Now - File.GetLastWriteTime(file) > s_retention)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
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
