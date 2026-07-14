using System.Diagnostics;
using TaskNotify.Core.Events;
using TaskNotify.Core.Tasks;
using TaskNotify.ProcessMonitor;
using Xunit;

namespace TaskNotify.Core.Tests;

public sealed class WmiFallbackIntegrationTests
{
    [Fact]
    public async Task Python_task_is_detected_when_wmi_is_unavailable_or_silent()
    {
        var tracker = new ProcessTaskTracker();
        var completion = new TaskCompletionSource<TaskCompletionNotice>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        CancellationTokenSource? snapshotCancellation = null;
        Task? snapshotTask = null;

        ValueTask Handle(ProcessLifecycleEvent processEvent, CancellationToken _)
        {
            var notice = tracker.Handle(processEvent);
            if (notice?.DisplayName == "python.exe") completion.TrySetResult(notice);
            return ValueTask.CompletedTask;
        }

        var snapshot = new SnapshotProcessMonitor();
        var wmi = new WmiProcessMonitor(
            _ =>
            {
                if (snapshotTask is not null) return;
                snapshotCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                snapshotTask = snapshot.RunAsync(Handle, snapshotCancellation.Token);
            },
            () => snapshotCancellation?.Cancel());
        var monitoring = wmi.RunAsync(Handle, cancellation.Token);

        await Task.Delay(TimeSpan.FromSeconds(4), cancellation.Token);
        using var host = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -Command \"python -c 'import time; time.sleep(25)'\"")
        {
            UseShellExecute = false
        });

        Assert.NotNull(host);
        try
        {
            var notice = await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
            Assert.Equal(TaskState.EndedUnknown, notice.State);
        }
        finally
        {
            cancellation.Cancel();
            snapshotCancellation?.Cancel();
            await monitoring;
            if (snapshotTask is not null) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => snapshotTask);
            if (!host.HasExited) host.Kill(true);
            snapshotCancellation?.Dispose();
        }
    }
}
