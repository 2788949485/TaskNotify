namespace TaskNotify.Core;

public enum TaskState
{
    Candidate,
    Running,
    WaitingForInput,
    WaitingForPermission,
    PossiblyCompleted,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    EndedUnknown,
    Ignored
}

public enum CompletionConfidence
{
    Unknown,
    Inferred,
    ProcessEnded,
    ExitCodeConfirmed,
    IntegrationConfirmed
}

public enum TaskSignal
{
    Started,
    WaitingForInput,
    WaitingForPermission,
    PossiblyCompleted,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    ProcessEnded,
    Ignored
}
