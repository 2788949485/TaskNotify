using System.Text.Json;
using TaskNotify.Ipc;

namespace TaskNotify.Integrations.Claude;

/// <summary>
/// Lightweight SDK for Claude Code to send task events to TaskNotify via Named Pipe.
///
/// Usage in Claude Code hooks:
///   var client = new ClaudeTaskNotifyClient();
///   await client.NotifyStartedAsync("refactor-auth", "Refactoring auth module");
///   await client.NotifySucceededAsync("refactor-auth");
///   await client.NotifyFailedAsync("refactor-auth", "Type error in UserService");
///   await client.NotifyWaitingForPermissionAsync("refactor-auth", "Run 'git push'?");
///
/// All methods are fire-and-forget: pipe failures are silently ignored.
/// Claude Code must never block waiting for TaskNotify.
/// </summary>
public sealed class ClaudeTaskNotifyClient : IDisposable
{
    private readonly IntegrationPipeClient _pipeClient = new();
    private readonly string? _source;

    public ClaudeTaskNotifyClient(string? source = null)
    {
        _source = source;
    }

    /// <summary>
    /// Reports that a Claude Code task has started.
    /// </summary>
    public async Task NotifyStartedAsync(string taskId, string? displayName = null, string? workingDir = null)
    {
        var msg = CreateMessage(IntegrationEventType.TaskStarted, taskId, displayName, workingDir);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports that a Claude Code task completed successfully.
    /// </summary>
    public async Task NotifySucceededAsync(string taskId, string? displayName = null, string? workingDir = null, int? exitCode = null)
    {
        var msg = CreateMessage(IntegrationEventType.TaskSucceeded, taskId, displayName, workingDir, exitCode: exitCode);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports that a Claude Code task failed.
    /// </summary>
    public async Task NotifyFailedAsync(string taskId, string? summary = null, string? displayName = null, string? workingDir = null, int exitCode = 1)
    {
        var msg = CreateMessage(IntegrationEventType.TaskFailed, taskId, displayName, workingDir, summary: summary, exitCode: exitCode);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports that Claude Code is waiting for user input.
    /// </summary>
    public async Task NotifyWaitingForInputAsync(string taskId, string summary, string? displayName = null, string? workingDir = null)
    {
        var msg = CreateMessage(IntegrationEventType.TaskWaitingForInput, taskId, displayName, workingDir, summary: summary);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports that Claude Code is waiting for user permission.
    /// </summary>
    public async Task NotifyWaitingForPermissionAsync(string taskId, string summary, string? displayName = null, string? workingDir = null)
    {
        var msg = CreateMessage(IntegrationEventType.TaskWaitingForPermission, taskId, displayName, workingDir, summary: summary);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports that a Claude Code task was cancelled.
    /// </summary>
    public async Task NotifyCancelledAsync(string taskId, string? displayName = null, string? workingDir = null)
    {
        var msg = CreateMessage(IntegrationEventType.TaskCancelled, taskId, displayName, workingDir);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports that a Claude Code task timed out.
    /// </summary>
    public async Task NotifyTimedOutAsync(string taskId, string? displayName = null, string? workingDir = null)
    {
        var msg = CreateMessage(IntegrationEventType.TaskTimedOut, taskId, displayName, workingDir);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    private IntegrationPipeMessage CreateMessage(
        IntegrationEventType type,
        string taskId,
        string? displayName,
        string? workingDir,
        string? summary = null,
        int? exitCode = null)
    {
        return new IntegrationPipeMessage
        {
            Type = type,
            Source = _source ?? "claude",
            TaskId = taskId,
            DisplayName = displayName,
            WorkingDir = workingDir,
            Summary = summary,
            ExitCode = exitCode
        };
    }

    public void Dispose() => _pipeClient.Dispose();
}
