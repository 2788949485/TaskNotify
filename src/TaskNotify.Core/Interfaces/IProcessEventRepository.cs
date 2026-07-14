using TaskNotify.Core.Events;

namespace TaskNotify.Core.Interfaces;

/// <summary>
/// Append-only store for raw process lifecycle events.
/// Useful for forensics and for re-deriving task state after a crash.
/// </summary>
public interface IProcessEventRepository
{
    /// <summary>Appends a raw process event row linked to a task.</summary>
    Task AppendAsync(int processId, int? parentProcessId, string processName, string eventType, DateTimeOffset eventTime, Guid? taskId = null, CancellationToken cancellationToken = default);

    /// <summary>Removes process events older than the cutoff. Returns rows deleted.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
