using System.Text.Json;
using TaskNotify.Ipc;

namespace TaskNotify.Integrations.Codex;

/// <summary>
/// Lightweight SDK for OpenAI Codex CLI to send task events to TaskNotify via Named Pipe.
///
/// Usage in Codex hooks:
///   var client = new CodexTaskNotifyClient();
///   await client.NotifyStartedAsync("build-project", "Building project");
///   await client.NotifySucceededAsync("build-project");
///   await client.NotifyFailedAsync("build-project", "Build failed with exit code 1");
///
/// Fire-and-forget: pipe failures are silently ignored.
/// </summary>
public sealed class CodexTaskNotifyClient : IDisposable
{
    private readonly IntegrationPipeClient _pipeClient = new();

    public async Task NotifyStartedAsync(string taskId, string? displayName = null, string? workingDir = null)
    {
        var msg = CreateMessage(IntegrationEventType.TaskStarted, taskId, displayName, workingDir);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public async Task NotifySucceededAsync(string taskId, string? displayName = null, string? workingDir = null, int? exitCode = null)
    {
        var msg = CreateMessage(IntegrationEventType.TaskSucceeded, taskId, displayName, workingDir, exitCode: exitCode);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public async Task NotifyFailedAsync(string taskId, string? summary = null, string? displayName = null, string? workingDir = null, int exitCode = 1)
    {
        var msg = CreateMessage(IntegrationEventType.TaskFailed, taskId, displayName, workingDir, summary: summary, exitCode: exitCode);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public async Task NotifyWaitingForPermissionAsync(string taskId, string summary, string? displayName = null, string? workingDir = null)
    {
        var msg = CreateMessage(IntegrationEventType.TaskWaitingForPermission, taskId, displayName, workingDir, summary: summary);
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public async Task NotifyCancelledAsync(string taskId, string? displayName = null, string? workingDir = null)
    {
        var msg = CreateMessage(IntegrationEventType.TaskCancelled, taskId, displayName, workingDir);
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
            Source = "codex",
            TaskId = taskId,
            DisplayName = displayName,
            WorkingDir = workingDir,
            Summary = summary,
            ExitCode = exitCode
        };
    }

    public void Dispose() => _pipeClient.Dispose();
}
