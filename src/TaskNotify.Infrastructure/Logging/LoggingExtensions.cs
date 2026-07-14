using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskNotify.Infrastructure.Logging;

namespace TaskNotify.Infrastructure;

public static class LoggingExtensions
{
    /// <summary>
    /// Adds a file logger with daily rotation and N-day retention.
    /// </summary>
    public static ILoggingBuilder AddTaskNotifyFileLogger(
        this ILoggingBuilder builder,
        string? logDirectory = null,
        int retentionDays = 14,
        LogLevel minLevel = LogLevel.Information)
    {
        builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(logDirectory, retentionDays, minLevel));
        return builder;
    }
}
