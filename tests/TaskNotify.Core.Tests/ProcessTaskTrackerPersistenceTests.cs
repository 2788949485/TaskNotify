using Microsoft.Extensions.Logging.Abstractions;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Events;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Performance;
using TaskNotify.Core.Recovery;
using TaskNotify.Core.Tasks;
using Xunit;

namespace TaskNotify.Core.Tests;

/// <summary>
/// Phase 1.1-1.3: verify ProcessTaskTracker persists state via IDetectedTaskRepository,
/// CapacityGuard drops candidates over the configured limit, and TaskRecoveryService
/// marks stale Running tasks as EndedUnknown on startup.
/// </summary>
public sealed class ProcessTaskTrackerPersistenceTests
{
    [Fact]
    public void Integration_lifecycle_persists_started_and_terminal_states()
    {
        var repo = new FakeTaskRepository();
        using var tracker = new ProcessTaskTracker(repo, eventRepository: null);

        var startedAt = DateTimeOffset.UtcNow;
        tracker.Handle(new IntegrationTaskEvent("claude", "task-1", "Claude", null,
            startedAt, IntegrationTaskAction.Started, null, null));
        tracker.Handle(new IntegrationTaskEvent("claude", "task-1", "Claude", null,
            startedAt.AddSeconds(2), IntegrationTaskAction.Succeeded, null, 0));

        // Dispose so the background save worker drains the queue.
        tracker.Dispose();

        Assert.True(repo.Saved.Count >= 2);
        var first = repo.Saved[0];
        Assert.Equal(TaskState.Running, first.State);

        var last = repo.Saved[^1];
        Assert.Equal(TaskState.Succeeded, last.State);
        Assert.Equal("claude", last.Source);
    }

    [Fact]
    public async Task Process_start_persists_running_task_for_wmi_root()
    {
        var repo = new FakeTaskRepository();
        using var tracker = new ProcessTaskTracker(repo, eventRepository: null);

        var start = DateTimeOffset.UtcNow;
        var root = new ProcessIdentity(9999, start);
        await tracker.Handle(new ProcessStartedEvent(root, 1, "python.exe", start, CommandLine: "python train.py"));

        tracker.Dispose();

        Assert.NotEmpty(repo.Saved);
        Assert.Equal("WMI", repo.Saved[0].Source);
        Assert.Equal(9999, repo.Saved[0].RootProcessId);
    }

    [Fact]
    public async Task Capacity_guard_drops_new_processes_when_over_limit()
    {
        var repo = new FakeTaskRepository();
        var guard = new CapacityGuard();
        // Pre-fill the guard to the limit.
        for (var i = 0; i < Limits.MaxTrackedProcesses; i++) guard.IncrementProcesses();

        using var tracker = new ProcessTaskTracker(repo, eventRepository: null, guard);

        var start = DateTimeOffset.UtcNow;
        var pid = new ProcessIdentity(4242, start);
        await tracker.Handle(new ProcessStartedEvent(pid, 1, "python.exe", start, CommandLine: "python over_limit.py"));

        Assert.Equal(Limits.MaxTrackedProcesses, guard.ActiveProcesses);
        Assert.Empty(repo.Saved);
    }

    [Fact]
    public async Task Recovery_service_marks_running_task_ended_unknown_when_process_is_gone()
    {
        var repo = new FakeTaskRepository();
        var task = new DetectedTask(
            Guid.NewGuid(),
            "WMI",
            "python.exe",
            50,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            RootProcessIdCannotExistOnAnyMachine);
        task.Apply(TaskSignal.Started, CompletionConfidence.Unknown, DateTimeOffset.UtcNow.AddMinutes(-10));
        await repo.SaveAsync(task, CancellationToken.None);
        Assert.Equal(TaskState.Running, task.State);

        var service = new TaskRecoveryService(repo, NullLogger<TaskRecoveryService>.Instance);
        var recovered = await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(1, recovered);
        var updated = await repo.FindByIdAsync(task.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(TaskState.EndedUnknown, updated!.State);
    }

    [Fact]
    public async Task Recovery_service_leaves_alive_task_untouched()
    {
        var repo = new FakeTaskRepository();
        var self = System.Diagnostics.Process.GetCurrentProcess();
        // Use the actual process start time so the recovery PID-reuse guard
        // recognises the live test-runner as the same process.
        var actualStart = new DateTimeOffset(self.StartTime.ToUniversalTime(), TimeSpan.Zero);
        var task = new DetectedTask(
            Guid.NewGuid(),
            "WMI",
            "test.exe",
            50,
            actualStart,
            self.Id);
        task.Apply(TaskSignal.Started, CompletionConfidence.Unknown, actualStart);
        await repo.SaveAsync(task, CancellationToken.None);

        var service = new TaskRecoveryService(repo, NullLogger<TaskRecoveryService>.Instance);
        var recovered = await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(0, recovered);
        var updated = await repo.FindByIdAsync(task.Id, CancellationToken.None);
        Assert.Equal(TaskState.Running, updated!.State);
    }

    /// <summary>
    /// PID space is 0..int.MaxValue on Windows; a PID this high is virtually never assigned.
    /// Process.GetProcessById on it throws ArgumentException, which is the "process gone" path.
    /// </summary>
    private const int RootProcessIdCannotExistOnAnyMachine = int.MaxValue - 7;

    private sealed record TaskSnapshot(Guid Id, string Source, TaskState State, int? RootProcessId);

    private sealed class FakeTaskRepository : IDetectedTaskRepository
    {
        public List<TaskSnapshot> Saved { get; } = [];
        private readonly Dictionary<Guid, DetectedTask> _store = new();

        public Task SaveAsync(DetectedTask task, CancellationToken cancellationToken = default)
        {
            _store[task.Id] = task;
            Saved.Add(new TaskSnapshot(task.Id, task.Source, task.State, task.RootProcessId));
            return Task.CompletedTask;
        }

        public Task<DetectedTask?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(id, out var task);
            return Task.FromResult(task);
        }

        public Task<IReadOnlyList<DetectedTask>> FindActiveAsync(CancellationToken cancellationToken = default)
        {
            var active = _store.Values
                .Where(t => !TaskStateMachine.IsTerminal(t.State))
                .ToList();
            return Task.FromResult<IReadOnlyList<DetectedTask>>(active);
        }

        public Task<IReadOnlyList<DetectedTask>> FindRecentAsync(int limit, CancellationToken cancellationToken = default)
        {
            var recent = _store.Values
                .Where(t => t.EndedAt is not null)
                .OrderByDescending(t => t.EndedAt!)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<DetectedTask>>(recent);
        }

        public Task<IReadOnlyList<DateTimeOffset>> FindRecentForProcessAsync(string processName, DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            var days = _store.Values
                .Where(t => string.Equals(t.ProcessName, processName, StringComparison.OrdinalIgnoreCase)
                            && t.EndedAt is not null && t.EndedAt >= since)
                .Select(t => ((DateTimeOffset)t.EndedAt!).ToUniversalTime().Date)
                .Distinct()
                .Select(d => new DateTimeOffset(d, TimeSpan.Zero))
                .ToList();
            return Task.FromResult<IReadOnlyList<DateTimeOffset>>(days);
        }

        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            var stale = _store.Values.Where(t => t.EndedAt is { } ended && ended < cutoff).ToList();
            foreach (var t in stale) _store.Remove(t.Id);
            return Task.FromResult(stale.Count);
        }

        public Task<int> PurgeAllAsync(CancellationToken cancellationToken = default)
        {
            var count = _store.Count;
            _store.Clear();
            return Task.FromResult(count);
        }
    }
}
