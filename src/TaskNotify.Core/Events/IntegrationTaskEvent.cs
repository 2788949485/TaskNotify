namespace TaskNotify.Core.Events;

/// <summary>
/// A standardized task event emitted by any integration layer
/// (Claude Code, Codex, Hermes Agent, VS Code, PowerShell).
///
/// This is the bridge between external integrations and the core ProcessTaskTracker.
/// Defined in Core so that Core owns the domain model without circular dependencies.
/// </summary>
public sealed record IntegrationTaskEvent(
    string Source,           // "claude", "codex", "hermes", "vscode", "powershell"
    string? TaskId,          // Stable task/session ID from the integration side
    string? DisplayName,     // Human-readable task name
    string? WorkingDir,      // Working directory
    DateTimeOffset OccurredAt,
    IntegrationTaskAction Action,
    string? Summary,         // Optional: why the agent needs user input, etc.
    int? ExitCode);          // Only meaningful for Succeeded/Failed

public enum IntegrationTaskAction
{
    /// <summary>
    /// Agent started a new task session.
    /// Transitions: Candidate → Running
    /// Confidence: IntegrationConfirmed (highest)
    /// </summary>
    Started,

    /// <summary>
    /// Agent completed the task successfully.
    /// Transitions: Running → Succeeded
    /// Confidence: IntegrationConfirmed
    /// </summary>
    Succeeded,

    /// <summary>
    /// Agent failed the task with an error.
    /// Transitions: Running → Failed
    /// Confidence: IntegrationConfirmed
    /// </summary>
    Failed,

    /// <summary>
    /// Agent is waiting for user input (e.g. Claude asking a question).
    /// Transitions: Running → WaitingForInput
    /// Confidence: Inferred
    /// </summary>
    WaitingForInput,

    /// <summary>
    /// Agent is waiting for user permission (e.g. Claude asking to run a command).
    /// Transitions: Running → WaitingForPermission
    /// Confidence: Inferred
    /// </summary>
    WaitingForPermission,

    /// <summary>
    /// Agent cancelled the task.
    /// Transitions: Running → Cancelled
    /// Confidence: IntegrationConfirmed
    /// </summary>
    Cancelled,

    /// <summary>
    /// Agent reports the task timed out.
    /// Transitions: Running → TimedOut
    /// Confidence: IntegrationConfirmed
    /// </summary>
    TimedOut,

    /// <summary>
    /// The command ended but no reliable result was supplied.
    /// </summary>
    EndedUnknown
}
