using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Core.Recovery;

/// <summary>
/// On startup, walks all tasks that were still Running when TaskNotify was killed,
/// checks whether their root process is still alive, and marks missing ones as
/// EndedUnknown. Per doc chapter 28.2 this path emits no notification — the user
/// has already moved on.
/// </summary>
public sealed class TaskRecoveryService
{
    private readonly IDetectedTaskRepository _repository;
    private readonly ILogger<TaskRecoveryService> _logger;

    public TaskRecoveryService(
        IDetectedTaskRepository repository,
        ILogger<TaskRecoveryService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<int> RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            var active = await _repository.FindActiveAsync(cancellationToken);
            if (active.Count == 0) return 0;

            var recovered = 0;
            foreach (var task in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryRecoverOne(task))
                {
                    try
                    {
                        await _repository.SaveAsync(task, cancellationToken);
                        recovered++;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(exception,
                            "Failed to persist recovered state for task {TaskId}.", task.Id);
                    }
                }
            }
            return recovered;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Task recovery scan failed; continuing without recovery.");
            return 0;
        }
    }

    private static bool TryRecoverOne(DetectedTask task)
    {
        if (task.State == TaskState.Candidate) return false;
        if (task.RootProcessId is not { } pid) return false;

        if (!IsProcessAlive(pid, task.StartedAt))
        {
            task.Apply(TaskSignal.ProcessEnded, CompletionConfidence.ProcessEnded, DateTimeOffset.UtcNow);
            return true;
        }

        return false;
    }

    private static bool IsProcessAlive(int pid, DateTimeOffset? expectedStartedAt)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
            if (process.HasExited) return false;

            // PID reuse guard: if the live process started much later than our
            // recorded task, the original is gone and the PID was reassigned.
            if (expectedStartedAt is { } expected)
            {
                try
                {
                    var actual = process.StartTime.ToUniversalTime();
                    if (actual > expected.AddSeconds(5)) return false;
                }
                catch (Exception)
                {
                    // StartTime access can throw on some system processes — assume alive.
                }
            }

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        finally
        {
            process?.Dispose();
        }
    }
}
