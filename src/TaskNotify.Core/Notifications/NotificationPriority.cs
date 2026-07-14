using TaskNotify.Core.Tasks;

namespace TaskNotify.Core.Notifications;

/// <summary>
/// Resolves the relative importance of a <see cref="TaskState"/> for toast-merging
/// purposes (doc chapter 22.3). When several events for the same task arrive inside
/// the merge window we keep only the highest-priority one so the user sees the
/// worst-case state, not a sequence of toasts.
/// </summary>
public static class NotificationPriority
{
    public static int Of(TaskState state) => state switch
    {
        TaskState.Failed => 100,
        TaskState.WaitingForPermission => 90,
        TaskState.WaitingForInput => 80,
        TaskState.Succeeded => 70,
        TaskState.Cancelled => 60,
        TaskState.TimedOut => 60,
        TaskState.EndedUnknown => 50,
        TaskState.PossiblyCompleted => 40,
        _ => 0
    };

    /// <summary>
    /// Returns the higher-priority notice. Ties keep the existing one so a late
    /// duplicate doesn't displace an already-shown toast.
    /// </summary>
    public static TaskCompletionNotice Pick(TaskCompletionNotice existing, TaskCompletionNotice incoming)
        => Of(incoming.State) > Of(existing.State) ? incoming : existing;
}
