namespace TaskNotify.Core;

public sealed record TaskCompletionNotice(Guid TaskId, string DisplayName, TimeSpan Duration, TaskState State);

public sealed class ProcessTaskTracker
{
    private readonly DetectionRuleEngine _ruleEngine = new();
    private readonly Dictionary<int, TrackedProcess> _processes = [];

    public TaskCompletionNotice? Handle(ProcessLifecycleEvent processEvent)
    {
        return processEvent switch
        {
            ProcessStartedEvent started => HandleStarted(started),
            ProcessStoppedEvent stopped => HandleStopped(stopped),
            _ => null
        };
    }

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
