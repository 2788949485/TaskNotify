using TaskNotify.Ipc;

namespace TaskNotify.Integrations.Hermes;

/// <summary>
/// Hermes Agent integration client.
///
/// When Hermes Agent (this bot) runs a long task, it can send events
/// directly to TaskNotify via the Named Pipe.
///
/// Usage:
///   var client = new HermesTaskNotifyClient();
///   await client.NotifyTaskStartedAsync("deploy", "Deploying to production", "Working dir...");
///   await client.NotifyWaitingForPermissionAsync("deploy", "Confirm deployment?");
///   await client.NotifyTaskSucceededAsync("deploy");
/// </summary>
public sealed class HermesTaskNotifyClient : IDisposable
{
    private readonly IntegrationPipeClient _pipeClient = new();

    public async Task NotifyTaskStartedAsync(string taskId, string? displayName = null, string? workingDir = null, string? summary = null)
    {
        var msg = new IntegrationPipeMessage
        {
            Type = IntegrationEventType.TaskStarted,
            Source = "hermes",
            TaskId = taskId,
            DisplayName = displayName,
            WorkingDir = workingDir,
            Summary = summary
        };
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public async Task NotifyTaskSucceededAsync(string taskId, string? displayName = null, string? workingDir = null, int? exitCode = null)
    {
        var msg = new IntegrationPipeMessage
        {
            Type = IntegrationEventType.TaskSucceeded,
            Source = "hermes",
            TaskId = taskId,
            DisplayName = displayName,
            WorkingDir = workingDir,
            ExitCode = exitCode
        };
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public async Task NotifyTaskFailedAsync(string taskId, string? summary = null, string? displayName = null, string? workingDir = null, int exitCode = 1)
    {
        var msg = new IntegrationPipeMessage
        {
            Type = IntegrationEventType.TaskFailed,
            Source = "hermes",
            TaskId = taskId,
            DisplayName = displayName,
            WorkingDir = workingDir,
            Summary = summary,
            ExitCode = exitCode
        };
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public async Task NotifyWaitingForPermissionAsync(string taskId, string summary, string? displayName = null, string? workingDir = null)
    {
        var msg = new IntegrationPipeMessage
        {
            Type = IntegrationEventType.TaskWaitingForPermission,
            Source = "hermes",
            TaskId = taskId,
            DisplayName = displayName,
            WorkingDir = workingDir,
            Summary = summary
        };
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public async Task NotifyWaitingForInputAsync(string taskId, string summary, string? displayName = null, string? workingDir = null)
    {
        var msg = new IntegrationPipeMessage
        {
            Type = IntegrationEventType.TaskWaitingForInput,
            Source = "hermes",
            TaskId = taskId,
            DisplayName = displayName,
            WorkingDir = workingDir,
            Summary = summary
        };
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public async Task NotifyTaskCancelledAsync(string taskId, string? displayName = null, string? workingDir = null)
    {
        var msg = new IntegrationPipeMessage
        {
            Type = IntegrationEventType.TaskCancelled,
            Source = "hermes",
            TaskId = taskId,
            DisplayName = displayName,
            WorkingDir = workingDir
        };
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public async Task NotifyTaskTimedOutAsync(string taskId, string? displayName = null, string? workingDir = null)
    {
        var msg = new IntegrationPipeMessage
        {
            Type = IntegrationEventType.TaskTimedOut,
            Source = "hermes",
            TaskId = taskId,
            DisplayName = displayName,
            WorkingDir = workingDir
        };
        await _pipeClient.SendAsync(msg).ConfigureAwait(false);
    }

    public void Dispose() => _pipeClient.Dispose();
}
