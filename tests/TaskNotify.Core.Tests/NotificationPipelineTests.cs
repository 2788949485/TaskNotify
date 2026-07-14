using Xunit;
using TaskNotify.Core.Notifications;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Core.Tests;

public class NotificationMergerTests
{
    private static readonly Guid TaskA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TaskB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static TaskCompletionNotice Notice(Guid id, TaskState state) =>
        new(id, id.ToString().Substring(0, 4), TimeSpan.FromSeconds(1), state);

    [Fact]
    public void FirstOfferReturnsNullButIsBuffered()
    {
        var merger = new NotificationMerger(TimeSpan.FromSeconds(5));
        var now = DateTimeOffset.UtcNow;

        merger.Offer(Notice(TaskA, TaskState.Succeeded), now);

        var drained = merger.DrainAll();
        Assert.Single(drained);
        Assert.Equal(TaskState.Succeeded, drained[0].State);
    }

    [Fact]
    public void HigherPriorityEventWithinWindowWins()
    {
        var merger = new NotificationMerger(TimeSpan.FromSeconds(5));
        var t0 = DateTimeOffset.UtcNow;

        merger.Offer(Notice(TaskA, TaskState.Succeeded), t0);
        // Same task, worse state, 1s later — within window.
        merger.Offer(Notice(TaskA, TaskState.Failed), t0.AddSeconds(1));

        var drained = merger.DrainAll();
        Assert.Single(drained);
        Assert.Equal(TaskState.Failed, drained[0].State);
    }

    [Fact]
    public void DifferentTasksDoNotInterfere()
    {
        var merger = new NotificationMerger(TimeSpan.FromSeconds(5));
        var t0 = DateTimeOffset.UtcNow;

        merger.Offer(Notice(TaskA, TaskState.Succeeded), t0);
        merger.Offer(Notice(TaskB, TaskState.Failed), t0);

        var drained = merger.DrainAll();
        Assert.Equal(2, drained.Count);
        Assert.Contains(drained, n => n.TaskId == TaskA && n.State == TaskState.Succeeded);
        Assert.Contains(drained, n => n.TaskId == TaskB && n.State == TaskState.Failed);
    }

    [Fact]
    public void DrainExpiredOnlyReturnsNoticesPastWindow()
    {
        var window = TimeSpan.FromSeconds(5);
        var merger = new NotificationMerger(window);
        var t0 = DateTimeOffset.UtcNow;

        merger.Offer(Notice(TaskA, TaskState.Succeeded), t0);
        // Inside the window — should not drain yet.
        Assert.Empty(merger.DrainExpired(t0.AddSeconds(2)));

        // Past the window — drains.
        var drained = merger.DrainExpired(t0.AddSeconds(6));
        Assert.Single(drained);
        Assert.Empty(merger.DrainAll());
    }

    [Fact]
    public void TieKeepsExistingState()
    {
        var merger = new NotificationMerger(TimeSpan.FromSeconds(5));
        var t0 = DateTimeOffset.UtcNow;

        merger.Offer(Notice(TaskA, TaskState.Failed), t0);
        merger.Offer(Notice(TaskA, TaskState.Succeeded), t0.AddSeconds(1));

        Assert.Equal(TaskState.Failed, merger.DrainAll()[0].State);
    }
}

public class NotificationCooldownTests
{
    private static readonly Guid TaskA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static TaskCompletionNotice Notice(Guid id, TaskState state) =>
        new(id, "x", TimeSpan.Zero, state);

    [Fact]
    public void FirstCallForTaskAcquires()
    {
        var cooldown = new NotificationCooldown(TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;

        Assert.True(cooldown.TryAcquire(Notice(TaskA, TaskState.Succeeded), now));
    }

    [Fact]
    public void SecondCallWithinWindowIsSuppressed()
    {
        var cooldown = new NotificationCooldown(TimeSpan.FromSeconds(10));
        var t0 = DateTimeOffset.UtcNow;

        Assert.True(cooldown.TryAcquire(Notice(TaskA, TaskState.Succeeded), t0));
        Assert.False(cooldown.TryAcquire(Notice(TaskA, TaskState.Failed), t0.AddSeconds(3)));
    }

    [Fact]
    public void CallAfterWindowAcquires()
    {
        var cooldown = new NotificationCooldown(TimeSpan.FromSeconds(10));
        var t0 = DateTimeOffset.UtcNow;

        Assert.True(cooldown.TryAcquire(Notice(TaskA, TaskState.Succeeded), t0));
        Assert.True(cooldown.TryAcquire(Notice(TaskA, TaskState.Succeeded), t0.AddSeconds(11)));
    }

    [Fact]
    public void WaitingForPermissionBypassesCooldown()
    {
        var cooldown = new NotificationCooldown(TimeSpan.FromSeconds(10));
        var t0 = DateTimeOffset.UtcNow;

        Assert.True(cooldown.TryAcquire(Notice(TaskA, TaskState.Succeeded), t0));
        // Same task, within window, but WaitingForPermission — should still fire.
        Assert.True(cooldown.TryAcquire(Notice(TaskA, TaskState.WaitingForPermission), t0.AddSeconds(1)));
    }

    [Fact]
    public void CompactDropsStaleEntries()
    {
        var cooldown = new NotificationCooldown(TimeSpan.FromSeconds(10));
        var t0 = DateTimeOffset.UtcNow;

        cooldown.TryAcquire(Notice(TaskA, TaskState.Succeeded), t0);
        cooldown.Compact(t0.AddSeconds(15));

        // After compact, the stale entry is gone — next call should acquire.
        Assert.True(cooldown.TryAcquire(Notice(TaskA, TaskState.Succeeded), t0.AddSeconds(15)));
    }
}

public class NotificationPriorityTests
{
    [Fact]
    public void FailedBeatsSucceeded()
    {
        var existing = new TaskCompletionNotice(Guid.NewGuid(), "a", TimeSpan.Zero, TaskState.Succeeded);
        var incoming = new TaskCompletionNotice(existing.TaskId, "a", TimeSpan.Zero, TaskState.Failed);

        Assert.Same(incoming, NotificationPriority.Pick(existing, incoming));
    }

    [Fact]
    public void WaitingForPermissionBeatsSucceeded()
    {
        var existing = new TaskCompletionNotice(Guid.NewGuid(), "a", TimeSpan.Zero, TaskState.Succeeded);
        var incoming = new TaskCompletionNotice(existing.TaskId, "a", TimeSpan.Zero, TaskState.WaitingForPermission);

        Assert.Same(incoming, NotificationPriority.Pick(existing, incoming));
    }

    [Fact]
    public void PossiblyCompletedLosesToEverything()
    {
        var existing = new TaskCompletionNotice(Guid.NewGuid(), "a", TimeSpan.Zero, TaskState.PossiblyCompleted);
        var incoming = new TaskCompletionNotice(existing.TaskId, "a", TimeSpan.Zero, TaskState.EndedUnknown);

        Assert.Same(incoming, NotificationPriority.Pick(existing, incoming));
    }
}
