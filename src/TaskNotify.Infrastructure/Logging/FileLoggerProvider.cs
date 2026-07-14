using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TaskNotify.Infrastructure.Logging;

/// <summary>
/// File-based logger that writes to a daily-rotated file under
/// <c>%LOCALAPPDATA%\TaskNotify\logs\</c>. Files older than the configured
/// retention window are deleted on open.
///
/// Thread-safe via a single background queue (does not block callers).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly int _retentionDays;
    private readonly LogLevel _minLevel;
    private readonly BlockingLogWriter _writer;

    public FileLoggerProvider(string? logDirectory, int retentionDays, LogLevel minLevel)
    {
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory) ? DefaultLogDirectory() : logDirectory;
        Directory.CreateDirectory(_logDirectory);
        _retentionDays = Math.Max(1, retentionDays);
        _minLevel = minLevel;
        _writer = new BlockingLogWriter(_logDirectory, _retentionDays);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _minLevel, _writer);

    public void Dispose() => _writer.Dispose();

    public static string DefaultLogDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "TaskNotify", "logs");
    }

    private sealed class FileLogger(string name, LogLevel minLevel, BlockingLogWriter writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = string.Format(
                CultureInfo.InvariantCulture,
                "[{0:O}] [{1}] [{2}] {3}{4}",
                DateTimeOffset.UtcNow,
                logLevel,
                name,
                formatter(state, exception),
                exception is null ? string.Empty : Environment.NewLine + exception);
            writer.Enqueue(message);
        }
    }

    private sealed class BlockingLogWriter : IDisposable
    {
        private readonly string _directory;
        private readonly int _retentionDays;
        private readonly BlockingCollection<string> _queue = new(4096);
        private readonly Task _pump;
        private readonly CancellationTokenSource _cts = new();

        public BlockingLogWriter(string directory, int retentionDays)
        {
            _directory = directory;
            _retentionDays = retentionDays;
            _pump = Task.Run(PumpAsync);
        }

        public void Enqueue(string line)
        {
            // Best-effort: if the queue is full, drop the message rather than block the caller.
            _queue.TryAdd(line);
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _cts.Cancel();
            try { _pump.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            _cts.Dispose();
            _queue.Dispose();
        }

        private async Task PumpAsync()
        {
            PurgeOldFiles();
            foreach (var line in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    var path = Path.Combine(_directory, $"tasknotify-{DateTime.UtcNow:yyyyMMdd}.log");
                    await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8).ConfigureAwait(false);
                }
                catch
                {
                    // Swallow: logging must never throw.
                }
            }
        }

        private void PurgeOldFiles()
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);
            try
            {
                foreach (var file in Directory.EnumerateFiles(_directory, "tasknotify-*.log"))
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
                    {
                        try { File.Delete(file); } catch { /* ignore */ }
                    }
                }
            }
            catch { /* ignore */ }
        }
    }
}
