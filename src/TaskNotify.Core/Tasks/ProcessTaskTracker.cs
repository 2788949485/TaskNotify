using TaskNotify.Core.Detection;
using TaskNotify.Core.Events;

namespace TaskNotify.Core.Tasks;

public sealed record TaskCompletionNotice(Guid TaskId, string DisplayName, TimeSpan Duration, TaskState State);

public sealed class ProcessTaskTracker
{
    private readonly DetectionRuleEngine _ruleEngine = new();
    private readonly object _gate = new();
    private readonly Dictionary<int, TrackedProcess> _processes = [];
    private readonly Dictionary<string, TrackedIntegration> _integrations = new(StringComparer.OrdinalIgnoreCase);

    public TaskCompletionNotice? Handle(ProcessLifecycleEvent processEvent)
    {
        lock (_gate)
        {
            return processEvent switch
            {
                ProcessStartedEvent started => HandleStarted(started),
                ProcessStoppedEvent stopped => HandleStopped(stopped),
                _ => null
            };
        }
    }

    /// <summary>
    /// Handles a standardized integration event (Claude Code, Codex, Hermes, etc.).
    /// Creates or updates a tracked integration task, transitions state,
    /// and produces a completion notice when the task reaches a terminal state.
    /// </summary>
    public TaskCompletionNotice? Handle(IntegrationTaskEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        lock (_gate)
        {
            return HandleIntegration(integrationEvent);
        }
    }

    private TaskCompletionNotice? HandleIntegration(IntegrationTaskEvent integrationEvent)
    {
        var taskKey = integrationEvent.TaskId ?? $"{integrationEvent.Source}:{integrationEvent.DisplayName}";

        if (!_integrations.TryGetValue(taskKey, out var tracked))
        {
            tracked = new TrackedIntegration(integrationEvent);
            _integrations[taskKey] = tracked;
            tracked.Task.Apply(TaskSignal.Started, CompletionConfidence.IntegrationConfirmed, integrationEvent.OccurredAt);
        }
        else
        {
            // Update display name if this is richer
            if (integrationEvent.DisplayName is not null && integrationEvent.DisplayName != tracked.Task.DisplayName)
            {
                tracked.Task.SetDisplayName(integrationEvent.DisplayName);
            }
        }

        var signal = MapActionToIntegrationSignal(integrationEvent.Action);
        var confidence = integrationEvent.Action switch
        {
            IntegrationTaskAction.Succeeded or IntegrationTaskAction.Failed or
            IntegrationTaskAction.Cancelled or IntegrationTaskAction.TimedOut
                => CompletionConfidence.IntegrationConfirmed,
            IntegrationTaskAction.EndedUnknown => CompletionConfidence.ProcessEnded,
            IntegrationTaskAction.WaitingForInput or IntegrationTaskAction.WaitingForPermission
                => CompletionConfidence.Inferred,
            _ => CompletionConfidence.Unknown
        };

        var startedAt = tracked.Task.StartedAt ?? integrationEvent.OccurredAt;
        var transitioned = tracked.Task.Apply(signal, confidence, integrationEvent.OccurredAt, integrationEvent.ExitCode);
        var duration = integrationEvent.OccurredAt - startedAt;

        if (integrationEvent.Action is IntegrationTaskAction.WaitingForInput or IntegrationTaskAction.WaitingForPermission)
        {
            var waitingState = integrationEvent.Action == IntegrationTaskAction.WaitingForPermission
                ? TaskState.WaitingForPermission
                : TaskState.WaitingForInput;
            return new(tracked.Task.Id, tracked.Task.DisplayName, duration, waitingState);
        }

        if (transitioned && TaskStateMachine.IsTerminal(tracked.Task.State))
        {
            if (integrationEvent.Source.Equals("powershell", StringComparison.OrdinalIgnoreCase) &&
                tracked.Task.State != TaskState.Failed &&
                duration < TimeSpan.FromSeconds(20))
            {
                _integrations.Remove(taskKey);
                return null;
            }

            var notice = new TaskCompletionNotice(
                tracked.Task.Id,
                tracked.Task.DisplayName,
                duration,
                tracked.Task.State);

            _integrations.Remove(taskKey);

            return notice;
        }

        return null;
    }

    private static TaskSignal MapActionToIntegrationSignal(IntegrationTaskAction action) => action switch
    {
        IntegrationTaskAction.Started => TaskSignal.Started,
        IntegrationTaskAction.Succeeded => TaskSignal.Succeeded,
        IntegrationTaskAction.Failed => TaskSignal.Failed,
        IntegrationTaskAction.WaitingForInput => TaskSignal.WaitingForInput,
        IntegrationTaskAction.WaitingForPermission => TaskSignal.WaitingForPermission,
        IntegrationTaskAction.Cancelled => TaskSignal.Cancelled,
        IntegrationTaskAction.TimedOut => TaskSignal.TimedOut,
        IntegrationTaskAction.EndedUnknown => TaskSignal.ProcessEnded,
        _ => TaskSignal.Started
    };

    private TaskCompletionNotice? HandleStarted(ProcessStartedEvent started)
    {
        _processes.TryGetValue(started.ParentProcessId ?? -1, out var parent);
        var candidate = new ProcessCandidate(
            started.Identity.ProcessId,
            started.Identity.StartedAt,
            started.ProcessName,
            started.ExecutablePath,
            started.CommandLine,
            started.ParentProcessName ?? parent?.Candidate.ProcessName);
        var result = _ruleEngine.Evaluate(candidate, TimeSpan.Zero, BuiltInDetectionRules.Balanced);
        if (result.Probability == TaskProbability.Ignored)
        {
            return null;
        }

        var group = parent?.Group ?? new ProcessTaskGroup(candidate, result.Score, started.OccurredAt);
        group.Add(started.Identity);
        _processes[started.Identity.ProcessId] = new(started.Identity, candidate, group);
        return null;
    }

    private TaskCompletionNotice? HandleStopped(ProcessStoppedEvent stopped)
    {
        if (!_processes.TryGetValue(stopped.ProcessId, out var process) ||
            stopped.Identity is not null && stopped.Identity != process.Identity)
        {
            return null;
        }

        _processes.Remove(stopped.ProcessId);

        process.Group.Remove(process.Identity);
        if (process.Identity.ProcessId == process.Group.RootProcessId)
        {
            process.Group.RootEnded = true;
        }

        if (!process.Group.RootEnded || process.Group.HasRunningProcesses)
        {
            return null;
        }

        var duration = stopped.OccurredAt - process.Group.StartedAt;
        var result = _ruleEngine.Evaluate(process.Group.RootCandidate, duration, BuiltInDetectionRules.Balanced);
        process.Group.Task.SetTaskProbability(result.Score);
        process.Group.Task.Apply(TaskSignal.ProcessEnded, CompletionConfidence.ProcessEnded, stopped.OccurredAt);

        return result.ShouldNotify
            ? new(process.Group.Task.Id, process.Group.Task.DisplayName, duration, process.Group.Task.State)
            : null;
    }

    private sealed record TrackedProcess(ProcessIdentity Identity, ProcessCandidate Candidate, ProcessTaskGroup Group);

    private sealed record TrackedIntegration(IntegrationTaskEvent InitialEvent)
    {
        public DetectedTask Task { get; } = new(
            Guid.NewGuid(),
            InitialEvent.Source,
            InitialEvent.DisplayName ?? "Integration Task",
            100,
            InitialEvent.OccurredAt,
            InitialEvent.ExitCode);
    }

    private sealed class ProcessTaskGroup
    {
        private readonly HashSet<ProcessIdentity> _runningProcesses = [];

        public ProcessTaskGroup(ProcessCandidate rootCandidate, int probability, DateTimeOffset startedAt)
        {
            RootCandidate = rootCandidate;
            RootProcessId = rootCandidate.ProcessId;
            StartedAt = startedAt;
            Task = new(Guid.NewGuid(), "WMI", rootCandidate.ProcessName, probability, startedAt, rootCandidate.ProcessId);
            Task.Apply(TaskSignal.Started, CompletionConfidence.Unknown, startedAt);
        }

        public ProcessCandidate RootCandidate { get; }
        public int RootProcessId { get; }
        public DateTimeOffset StartedAt { get; }
        public DetectedTask Task { get; }
        public bool RootEnded { get; set; }
        public bool HasRunningProcesses => _runningProcesses.Count > 0;

        public void Add(ProcessIdentity identity) => _runningProcesses.Add(identity);
        public void Remove(ProcessIdentity identity) => _runningProcesses.Remove(identity);
    }
}
