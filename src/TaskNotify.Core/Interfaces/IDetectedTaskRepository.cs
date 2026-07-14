using TaskNotify.Core.Tasks;

namespace TaskNotify.Core.Interfaces;

/// <summary>
/// Persistence contract for DetectedTask aggregate.
/// Implementations must be thread-safe and non-blocking from the caller's perspective
/// (use internal queuing if the underlying store is slow).
/// </summary>
public interface IDetectedTaskRepository
{
    /// <summary>Inserts a new task or updates an existing one by Id.</summary>
    Task SaveAsync(DetectedTask task, CancellationToken cancellationToken = default);

    /// <summary>Returns the task with the given id, or null.</summary>
    Task<DetectedTask?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns tasks currently in a non-terminal state.</summary>
    Task<IReadOnlyList<DetectedTask>> FindActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns recent terminal tasks ordered by EndedAt descending.</summary>
    Task<IReadOnlyList<DetectedTask>> FindRecentAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns distinct days (UTC dates) on which the given process name produced a
    /// recorded task in the window [since, now]. Used by ResidentProcessDetector.
    /// </summary>
    Task<IReadOnlyList<DateTimeOffset>> FindRecentForProcessAsync(
        string processName,
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes tasks that ended before the cutoff. Returns rows deleted.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>Removes all tasks. Used by privacy "clear history" action.</summary>
    Task<int> PurgeAllAsync(CancellationToken cancellationToken = default);
}
