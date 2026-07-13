using TaskNotify.Core;
using Xunit;

namespace TaskNotify.Core.Tests;

public sealed class CoreBehaviorTests
{
    [Fact]
    public void Process_end_without_reliable_result_becomes_ended_unknown()
    {
        var task = new DetectedTask(Guid.NewGuid(), "WMI", "Python", 50, DateTimeOffset.UtcNow);

        Assert.True(task.Apply(TaskSignal.Started, CompletionConfidence.ProcessEnded, DateTimeOffset.UtcNow));
        Assert.True(task.Apply(TaskSignal.ProcessEnded, CompletionConfidence.ProcessEnded, DateTimeOffset.UtcNow));

        Assert.Equal(TaskState.EndedUnknown, task.State);
    }

    [Fact]
    public void Unreliable_success_cannot_be_applied()
    {
        Assert.False(TaskStateMachine.TryTransition(
            TaskState.Running, TaskSignal.Succeeded, CompletionConfidence.ProcessEnded, out _));
    }

    [Fact]
    public void Terminal_failure_is_not_overwritten_by_later_success()
    {
        var task = new DetectedTask(Guid.NewGuid(), "Shell", "Build", 80, DateTimeOffset.UtcNow);
        task.Apply(TaskSignal.Started, CompletionConfidence.IntegrationConfirmed, DateTimeOffset.UtcNow);
        task.Apply(TaskSignal.Failed, CompletionConfidence.ExitCodeConfirmed, DateTimeOffset.UtcNow, 1);

        Assert.False(task.Apply(TaskSignal.Succeeded, CompletionConfidence.IntegrationConfirmed, DateTimeOffset.UtcNow, 0));
        Assert.Equal(TaskState.Failed, task.State);
    }

    [Fact]
    public void User_ignore_rule_wins_over_builtin_python_rule()
    {
        var candidate = new ProcessCandidate(10, DateTimeOffset.UtcNow, "python.exe", CommandLine: "python work.py");
        var userRule = new DetectionRule("用户忽略", Action: RuleAction.Ignore, ProcessNamePattern: "python\\.exe", IsUserCreated: true);

        var result = new DetectionRuleEngine().Evaluate(candidate, TimeSpan.FromMinutes(1), BuiltInDetectionRules.Balanced, [userRule]);

        Assert.Equal(TaskProbability.Ignored, result.Probability);
        Assert.Contains("用户忽略", result.MatchedRules);
    }

    [Fact]
    public void User_always_notify_rule_overrides_builtin_exclusion_after_the_default_threshold()
    {
        var candidate = new ProcessCandidate(10, DateTimeOffset.UtcNow, "python.exe", CommandLine: "python debugpy_worker.py");
        var userRule = new DetectionRule("用户始终提醒", Action: RuleAction.AlwaysNotify, ProcessNamePattern: "python\\.exe", IsUserCreated: true);

        var result = new DetectionRuleEngine().Evaluate(candidate, TimeSpan.FromSeconds(20), BuiltInDetectionRules.Balanced, [userRule]);

        Assert.True(result.ShouldNotify);
    }

    [Fact]
    public void Parent_and_child_processes_produce_one_unknown_result_after_both_end()
    {
        var tracker = new ProcessTaskTracker();
        var start = DateTimeOffset.UtcNow;
        var parent = new ProcessIdentity(100, start);
        var child = new ProcessIdentity(101, start.AddSeconds(1));

        tracker.Handle(new ProcessStartedEvent(parent, 1, "python.exe", start, ParentProcessName: "pwsh.exe"));
        tracker.Handle(new ProcessStartedEvent(child, 100, "ffmpeg.exe", start.AddSeconds(1)));

        Assert.Null(tracker.Handle(new ProcessStoppedEvent(100, "python.exe", start.AddSeconds(25), parent)));
        var notice = tracker.Handle(new ProcessStoppedEvent(101, "ffmpeg.exe", start.AddSeconds(26), child));

        Assert.NotNull(notice);
        Assert.Equal(TaskState.EndedUnknown, notice.State);
        Assert.Equal("python.exe", notice.DisplayName);
    }

    [Fact]
    public void A_stale_stop_event_cannot_end_a_reused_process_id()
    {
        var tracker = new ProcessTaskTracker();
        var first = new ProcessIdentity(100, DateTimeOffset.UtcNow);
        var replacement = new ProcessIdentity(100, first.StartedAt.AddMinutes(1));

        tracker.Handle(new ProcessStartedEvent(first, 1, "python.exe", first.StartedAt));
        tracker.Handle(new ProcessStartedEvent(replacement, 1, "python.exe", replacement.StartedAt));

        Assert.Null(tracker.Handle(new ProcessStoppedEvent(100, "python.exe", replacement.StartedAt.AddSeconds(30), first)));
    }

    [Theory]
    [InlineData("python upload.py --token abc", "python upload.py --token ***")]
    [InlineData("OPENAI_API_KEY=secret python run.py", "OPENAI_API_KEY=*** python run.py")]
    [InlineData("tool --password=\"secret value\"", "tool --password=***")]
    public void Sensitive_command_values_are_sanitized_before_storage(string command, string expected)
    {
        Assert.Equal(expected, CommandSanitizer.Sanitize(command));
    }
}
