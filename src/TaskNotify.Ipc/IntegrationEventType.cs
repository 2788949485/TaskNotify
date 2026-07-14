namespace TaskNotify.Ipc;

/// <summary>
/// Integration event types that external AI agents can send via Named Pipe.
/// </summary>
public enum IntegrationEventType
{
    /// <summary>
    /// Agent task started (e.g. Claude Code beginning a session).
    /// </summary>
    TaskStarted,

    /// <summary>
    /// Agent task completed successfully.
    /// </summary>
    TaskSucceeded,

    /// <summary>
    /// Agent task failed with an error.
    /// </summary>
    TaskFailed,

    /// <summary>
    /// Agent needs user input before continuing.
    /// </summary>
    TaskWaitingForInput,

    /// <summary>
    /// Agent needs user permission before continuing.
    /// </summary>
    TaskWaitingForPermission,

    /// <summary>
    /// Agent cancelled the task.
    /// </summary>
    TaskCancelled,

    /// <summary>
    /// Agent reports the task timed out.
    /// </summary>
    TaskTimedOut,

    /// <summary>
    /// The command ended but its result cannot be confirmed.
    /// </summary>
    TaskEndedUnknown,

    /// <summary>
    /// Health-check ping from an integration client.
    /// </summary>
    Ping
}
