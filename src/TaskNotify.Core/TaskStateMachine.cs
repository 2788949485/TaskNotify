namespace TaskNotify.Core;

public static class TaskStateMachine
{
    public static bool IsTerminal(TaskState state) => state is
        TaskState.Succeeded or TaskState.Failed or TaskState.Cancelled or
        TaskState.TimedOut or TaskState.EndedUnknown or TaskState.Ignored;

    public static bool TryTransition(
        TaskState current,
        TaskSignal signal,
        CompletionConfidence confidence,
        out TaskState next)
    {
        next = current;
        if (IsTerminal(current))
        {
            return current == StateFor(signal);
        }

        if ((signal is TaskSignal.Succeeded or TaskSignal.Failed) &&
            confidence < CompletionConfidence.ExitCodeConfirmed)
        {
            return false;
        }

        next = signal switch
        {
            TaskSignal.Started when current is TaskState.Candidate or TaskState.WaitingForInput or TaskState.WaitingForPermission => TaskState.Running,
            TaskSignal.WaitingForInput when current == TaskState.Running => TaskState.WaitingForInput,
            TaskSignal.WaitingForPermission when current == TaskState.Running => TaskState.WaitingForPermission,
            TaskSignal.PossiblyCompleted when current == TaskState.Running => TaskState.PossiblyCompleted,
            TaskSignal.Succeeded when current is TaskState.Running or TaskState.WaitingForInput or TaskState.WaitingForPermission or TaskState.PossiblyCompleted => TaskState.Succeeded,
            TaskSignal.Failed when current is TaskState.Running or TaskState.WaitingForInput or TaskState.WaitingForPermission or TaskState.PossiblyCompleted => TaskState.Failed,
            TaskSignal.Cancelled when current != TaskState.Candidate => TaskState.Cancelled,
            TaskSignal.TimedOut when current != TaskState.Candidate => TaskState.TimedOut,
            TaskSignal.ProcessEnded when current != TaskState.Candidate && confidence >= CompletionConfidence.ProcessEnded => TaskState.EndedUnknown,
            TaskSignal.Ignored => TaskState.Ignored,
            _ => current
        };

        return next != current;
    }

    private static TaskState? StateFor(TaskSignal signal) => signal switch
    {
        TaskSignal.Succeeded => TaskState.Succeeded,
        TaskSignal.Failed => TaskState.Failed,
        TaskSignal.Cancelled => TaskState.Cancelled,
        TaskSignal.TimedOut => TaskState.TimedOut,
        TaskSignal.ProcessEnded => TaskState.EndedUnknown,
        TaskSignal.Ignored => TaskState.Ignored,
        _ => null
    };
}
