using System.Collections.Concurrent;
using System.Security.Cryptography;
using TaskNotify.Core;
using TaskNotify.Ipc;

namespace TaskNotify.Integrations;

/// <summary>
/// Converts IntegrationPipeMessage (from Named Pipe) into IntegrationTaskEvent (core domain).
/// Handles deduplication via eventId and maps pipe message fields to standard events.
/// </summary>
public sealed class IntegrationEventConverter
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seenEventIds = new();
    private readonly TimeSpan _eventIdTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Converts a pipe message to a standardized integration event.
    /// Returns null if the event is a duplicate or cannot be converted.
    /// </summary>
    public IntegrationTaskEvent? Map(IntegrationPipeMessage pipeMsg)
    {
        if (pipeMsg is null) return null;

        // Deduplicate by eventId
        if (!_seenEventIds.TryAdd(pipeMsg.EventId, DateTimeOffset.UtcNow))
        {
            CleanupExpiredIds();
            return null;
        }

        var action = MapAction(pipeMsg.Type);
        if (action is null) return null;

        var displayName = pipeMsg.DisplayName ?? pipeMsg.Summary ?? $"{pipeMsg.Source ?? "unknown"} task";

        return new IntegrationTaskEvent(
            Source: pipeMsg.Source ?? "unknown",
            TaskId: pipeMsg.TaskId ?? HashString(displayName),
            DisplayName: displayName,
            WorkingDir: pipeMsg.WorkingDir,
            OccurredAt: DateTimeOffset.UtcNow,
            Action: action.Value,
            Summary: pipeMsg.Summary,
            ExitCode: pipeMsg.ExitCode);
    }

    private void CleanupExpiredIds()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _seenEventIds)
        {
            if (now - kvp.Value > _eventIdTtl)
            {
                _seenEventIds.TryRemove(kvp.Key, out _);
            }
        }
    }

    private static IntegrationTaskAction? MapAction(IntegrationEventType type) => type switch
    {
        IntegrationEventType.TaskStarted => IntegrationTaskAction.Started,
        IntegrationEventType.TaskSucceeded => IntegrationTaskAction.Succeeded,
        IntegrationEventType.TaskFailed => IntegrationTaskAction.Failed,
        IntegrationEventType.TaskWaitingForInput => IntegrationTaskAction.WaitingForInput,
        IntegrationEventType.TaskWaitingForPermission => IntegrationTaskAction.WaitingForPermission,
        IntegrationEventType.TaskCancelled => IntegrationTaskAction.Cancelled,
        IntegrationEventType.TaskTimedOut => IntegrationTaskAction.TimedOut,
        IntegrationEventType.TaskEndedUnknown => IntegrationTaskAction.EndedUnknown,
        IntegrationEventType.Ping => null,
        _ => null
    };

    private static string HashString(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant()[..8];
    }
}
