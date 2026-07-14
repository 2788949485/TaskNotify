using TaskNotify.Core.Tasks;

namespace TaskNotify.Core.Notifications;

/// <summary>
/// Merges rapid consecutive notifications for the same task into a single toast
/// (doc chapter 22.3). Within the configured burst window, the highest-priority
/// state wins; once the window closes the buffered notice is dispatched.
///
/// Thread-safe. Designed to be polled by the host on a timer or poked explicitly
/// when the next event arrives.
/// </summary>
public sealed class NotificationMerger
{
    private readonly TimeSpan _window;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Bucket> _buckets = new();

    public NotificationMerger(TimeSpan? window = null)
    {
        _window = window is { } w && w > TimeSpan.Zero ? w : TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Offer an incoming notice. Returns a notice to dispatch now (highest-priority
    /// event whose window expired), or <c>null</c> if the event was folded into an
    /// active window. Callers should also poll <see cref="DrainExpired"/> periodically.
    /// </summary>
    public TaskCompletionNotice? Offer(TaskCompletionNotice notice, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notice);
        lock (_gate)
        {
            if (_buckets.TryGetValue(notice.TaskId, out var bucket))
            {
                bucket.Notice = NotificationPriority.Pick(bucket.Notice, notice);
                return null;
            }

            _buckets[notice.TaskId] = new(notice, now + _window);
            return null;
        }
    }

    /// <summary>Returns notices whose burst window has elapsed and removes them from the buffer.</summary>
    public IReadOnlyList<TaskCompletionNotice> DrainExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_buckets.Count == 0) return Array.Empty<TaskCompletionNotice>();

            var result = new List<TaskCompletionNotice>();
            var dead = new List<Guid>();
            foreach (var (id, bucket) in _buckets)
            {
                if (bucket.Deadline <= now)
                {
                    result.Add(bucket.Notice);
                    dead.Add(id);
                }
            }
            foreach (var id in dead) _buckets.Remove(id);
            return result;
        }
    }

    /// <summary>For tests / shutdown: flush everything still buffered.</summary>
    public IReadOnlyList<TaskCompletionNotice> DrainAll()
    {
        lock (_gate)
        {
            var result = _buckets.Values.Select(b => b.Notice).ToList();
            _buckets.Clear();
            return result;
        }
    }

    private sealed record Bucket(TaskCompletionNotice Notice, DateTimeOffset Deadline)
    {
        public TaskCompletionNotice Notice { get; set; } = Notice;
        public DateTimeOffset Deadline { get; } = Deadline;
    }
}
