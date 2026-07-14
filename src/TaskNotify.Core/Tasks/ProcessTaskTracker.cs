using System.Threading.Channels;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Events;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Performance;

namespace TaskNotify.Core.Tasks;

public sealed record TaskCompletionNotice(Guid TaskId, string DisplayName, TimeSpan Duration, TaskState State);

public sealed class ProcessTaskTracker : IDisposable
{
    private readonly DetectionRuleEngine _ruleEngine = new();
    private readonly DetectionMode _detectionMode;
    private readonly ResidentProcessDetector? _residentDetector;
    private readonly object _gate = new();
    private readonly Dictionary<int, TrackedProcess> _processes = [];
    private readonly Dictionary<string, TrackedIntegration> _integrations = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDetectedTaskRepository? _taskRepository;
    private readonly IProcessEventRepository? _eventRepository;
    private readonly CapacityGuard? _capacity;
    private readonly Channel<DetectedTask> _taskSaveQueue;
    private readonly Channel<ProcessEventEntry> _eventAppendQueue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _taskSaveWorker;
    private readonly Task _eventAppendWorker;
    private bool _disposed;

    public ProcessTaskTracker() : this(null, null, null, DetectionMode.Balanced, null) { }

    public ProcessTaskTracker(
        IDetectedTaskRepository? taskRepository,
        IProcessEventRepository? eventRepository,
        CapacityGuard? capacity = null,
        DetectionMode detectionMode = DetectionMode.Balanced,
        ResidentProcessDetector? residentDetector = null)
    {
        _taskRepository = taskRepository;
        _eventRepository = eventRepository;
        _capacity = capacity;
        _detectionMode = detectionMode;
        _residentDetector = residentDetector;

        _taskSaveQueue = Channel.CreateUnbounded<DetectedTask>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _eventAppendQueue = Channel.CreateUnbounded<ProcessEventEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _taskSaveWorker = taskRepository is null ? Task.CompletedTask : Task.Run(TaskSaveLoopAsync);
        _eventAppendWorker = eventRepository is null ? Task.CompletedTask : Task.Run(EventAppendLoopAsync);
    }

    public async Task<TaskCompletionNotice?> Handle(ProcessLifecycleEvent processEvent, CancellationToken cancellationToken = default)
    {
        TaskCompletionNotice? notice = null;
        ProcessCandidate? candidate = null;

        lock (_gate)
        {
            switch (processEvent)
            {
                case ProcessStartedEvent started:
                    HandleStarted(started);
                    break;
                case ProcessStoppedEvent stopped:
                    (notice, candidate) = HandleStopped(stopped);
                    break;
            }
        }

        if (notice is null || candidate is null)
        {
            return notice;
        }

        // Resident check happens outside the lock — it does DB access for the
        // 7-day history condition (doc chapter 11.3). Per doc 28.2 if any
        // resident condition holds we suppress the notification silently.
        if (_residentDetector is not null)
        {
            try
            {
                var resident = await _residentDetector
                    .EvaluateAsync(candidate, processEvent.OccurredAt, cancellationToken)
                    .ConfigureAwait(false);
                if (resident.IsResident)
                {
                    return null;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Resident check failure: don't block notification.
            }
        }

        return notice;
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
            EnqueueTaskSave(tracked.Task);
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

        if (transitioned)
        {
            EnqueueTaskSave(tracked.Task);
        }

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
        if (_capacity is not null && !_capacity.CanAcceptProcess())
        {
            return null;
        }

        _processes.TryGetValue(started.ParentProcessId ?? -1, out var parent);
        var candidate = new ProcessCandidate(
            started.Identity.ProcessId,
            started.Identity.StartedAt,
            started.ProcessName,
            started.ExecutablePath,
            started.CommandLine,
            started.ParentProcessName ?? parent?.Candidate.ProcessName);
        var result = _ruleEngine.Evaluate(candidate, TimeSpan.Zero, BuiltInDetectionRules.For(_detectionMode));
        if (result.Probability == TaskProbability.Ignored)
        {
            return null;
        }

        var isNewGroup = parent?.Group is null;
        if (isNewGroup && _capacity is not null && !_capacity.CanAcceptGroup())
        {
            return null;
        }

        var group = parent?.Group ?? new ProcessTaskGroup(candidate, result.Score, started.OccurredAt);
        group.Add(started.Identity);
        _processes[started.Identity.ProcessId] = new(started.Identity, candidate, group);

        if (_capacity is not null)
        {
            _capacity.IncrementProcesses();
            if (isNewGroup) _capacity.IncrementGroups();
        }

        if (isNewGroup)
        {
            EnqueueTaskSave(group.Task);
        }
        AppendProcessEvent(started.ProcessName, started.Identity.ProcessId, started.ParentProcessId, "Started", started.OccurredAt, group.Task.Id);

        return null;
    }

    private (TaskCompletionNotice?, ProcessCandidate?) HandleStopped(ProcessStoppedEvent stopped)
    {
        if (!_processes.TryGetValue(stopped.ProcessId, out var process) ||
            stopped.Identity is not null && stopped.Identity != process.Identity)
        {
            return (null, null);
        }

        _processes.Remove(stopped.ProcessId);
        _capacity?.DecrementProcesses();

        process.Group.Remove(process.Identity);
        if (process.Identity.ProcessId == process.Group.RootProcessId)
        {
            process.Group.RootEnded = true;
        }

        AppendProcessEvent(stopped.ProcessName, stopped.ProcessId, null, "Stopped", stopped.OccurredAt, process.Group.Task.Id);

        if (!process.Group.RootEnded || process.Group.HasRunningProcesses)
        {
            return (null, null);
        }

        // Group fully drained — release the group slot.
        _capacity?.DecrementGroups();

        var duration = stopped.OccurredAt - process.Group.StartedAt;
        var result = _ruleEngine.Evaluate(process.Group.RootCandidate, duration, BuiltInDetectionRules.For(_detectionMode));
        process.Group.Task.SetTaskProbability(result.Score);
        process.Group.Task.Apply(TaskSignal.ProcessEnded, CompletionConfidence.ProcessEnded, stopped.OccurredAt);
        EnqueueTaskSave(process.Group.Task);

        return result.ShouldNotify
            ? (new(process.Group.Task.Id, process.Group.Task.DisplayName, duration, process.Group.Task.State), process.Group.RootCandidate)
            : (null, process.Group.RootCandidate);
    }

    private void EnqueueTaskSave(DetectedTask task)
    {
        if (_taskRepository is null) return;
        // Snapshot current state so later mutations don't bleed into this queued save.
        _taskSaveQueue.Writer.TryWrite(task.Clone());
    }

    private void AppendProcessEvent(
        string processName,
        int processId,
        int? parentProcessId,
        string eventType,
        DateTimeOffset eventTime,
        Guid? taskId)
    {
        if (_eventRepository is null) return;
        _eventAppendQueue.Writer.TryWrite(new ProcessEventEntry(
            processId, parentProcessId, processName, eventType, eventTime, taskId));
    }

    private async Task TaskSaveLoopAsync()
    {
        try
        {
            await foreach (var task in _taskSaveQueue.Reader.ReadAllAsync(_cts.Token))
            {
                try { await _taskRepository!.SaveAsync(task, _cts.Token); }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested) { throw; }
                catch { /* persistence failure is non-fatal */ }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
    }

    private async Task EventAppendLoopAsync()
    {
        try
        {
            await foreach (var entry in _eventAppendQueue.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    await _eventRepository!.AppendAsync(
                        entry.ProcessId,
                        entry.ParentProcessId,
                        entry.ProcessName,
                        entry.EventType,
                        entry.EventTime,
                        entry.TaskId,
                        _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested) { throw; }
                catch { /* event log failure is non-fatal */ }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _taskSaveQueue.Writer.TryComplete();
        _eventAppendQueue.Writer.TryComplete();

        try { _taskSaveWorker.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _eventAppendWorker.Wait(TimeSpan.FromSeconds(2)); } catch { }

        _cts.Cancel();
        _cts.Dispose();
    }

    private sealed record ProcessEventEntry(
        int ProcessId,
        int? ParentProcessId,
        string ProcessName,
        string EventType,
        DateTimeOffset EventTime,
        Guid? TaskId);

    private sealed record TrackedProcess(ProcessIdentity Identity, ProcessCandidate Candidate, ProcessTaskGroup Group);

    private sealed record TrackedIntegration(IntegrationTaskEvent InitialEvent)
    {
        public DetectedTask Task { get; } = new(
            Guid.NewGuid(),
            InitialEvent.Source,
            InitialEvent.DisplayName ?? "Integration Task",
            100,
            InitialEvent.OccurredAt,
            InitialEvent.ExitCode,
            workingDirectory: InitialEvent.WorkingDir,
            correlationKey: InitialEvent.TaskId);
    }

    private sealed class ProcessTaskGroup
    {
        private readonly HashSet<ProcessIdentity> _runningProcesses = [];

        public ProcessTaskGroup(ProcessCandidate rootCandidate, int probability, DateTimeOffset startedAt)
        {
            RootCandidate = rootCandidate;
            RootProcessId = rootCandidate.ProcessId;
            StartedAt = startedAt;
            Task = new(Guid.NewGuid(), "WMI", rootCandidate.ProcessName, probability, startedAt, rootCandidate.ProcessId, processName: rootCandidate.ProcessName);
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
