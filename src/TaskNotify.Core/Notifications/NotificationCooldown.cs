using TaskNotify.Core.Tasks;

namespace TaskNotify.Core.Notifications;

/// <summary>
/// Per-task notification cooldown (doc chapter 22.4). After a notice for task X is
/// dispatched, subsequent notices within <c>_cooldown</c> are suppressed — unless
/// the incoming event is WaitingForPermission, which always fires (the user must
/// take action).
/// </summary>
public sealed class NotificationCooldown
{
    private readonly TimeSpan _cooldown;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastDispatched = new();

    public NotificationCooldown(TimeSpan? cooldown = null)
    {
        _cooldown = cooldown is { } c && c > TimeSpan.Zero ? c : TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Returns true if the notice may be dispatched now. On success, records the
    /// dispatch time so subsequent calls within the window are suppressed.
    /// </summary>
    public bool TryAcquire(TaskCompletionNotice notice, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (notice.State is TaskState.WaitingForPermission) return true;

        lock (_gate)
        {
            if (_lastDispatched.TryGetValue(notice.TaskId, out var last) && now - last < _cooldown)
            {
                return false;
            }
            _lastDispatched[notice.TaskId] = now;
            return true;
        }
    }

    /// <summary>Removes entries older than the cooldown so the dictionary can't grow unbounded.</summary>
    public void Compact(DateTimeOffset now)
    {
        lock (_gate)
        {
            var stale = _lastDispatched.Where(p => now - p.Value >= _cooldown).ToList();
            foreach (var p in stale) _lastDispatched.Remove(p.Key);
        }
    }
}
