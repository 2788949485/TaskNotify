namespace TaskNotify.Core.Performance;

/// <summary>
/// Lightweight counter that tracks how many processes and task groups the
/// <see cref="Tasks.ProcessTaskTracker"/> currently holds. Use TryAcquire before
/// adding a new candidate; if it returns false the host is over capacity and the
/// candidate must be dropped silently (doc chapter 29).
///
/// The tracker holds the authoritative count via its dictionaries; this guard
/// exists so other services (diagnostics, UI) can read live counts without
/// reaching into tracker internals.
/// </summary>
public sealed class CapacityGuard
{
    private int _activeProcesses;
    private int _activeGroups;

    public int ActiveProcesses => Volatile.Read(ref _activeProcesses);
    public int ActiveGroups => Volatile.Read(ref _activeGroups);

    public bool CanAcceptProcess() => Volatile.Read(ref _activeProcesses) < Limits.MaxTrackedProcesses;
    public bool CanAcceptGroup() => Volatile.Read(ref _activeGroups) < Limits.MaxConcurrentTaskGroups;

    public void IncrementProcesses() => Interlocked.Increment(ref _activeProcesses);
    public void DecrementProcesses() => Interlocked.Decrement(ref _activeProcesses);
    public void IncrementGroups() => Interlocked.Increment(ref _activeGroups);
    public void DecrementGroups() => Interlocked.Decrement(ref _activeGroups);

    public void Reset()
    {
        Interlocked.Exchange(ref _activeProcesses, 0);
        Interlocked.Exchange(ref _activeGroups, 0);
    }
}
