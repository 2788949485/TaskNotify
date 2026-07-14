using System.Diagnostics;
using TaskNotify.Core.Events;
using TaskNotify.Core.Tasks;
using TaskNotify.ProcessMonitor;
using Xunit;

namespace TaskNotify.Core.Tests;

public sealed class SnapshotProcessMonitorTests
{
    [Fact]
    public async Task Twenty_five_second_python_task_from_powershell_produces_a_notice()
    {
        var tracker = new ProcessTaskTracker();
        var completion = new TaskCompletionSource<TaskCompletionNotice>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var host = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -Command \"python -c 'import time; time.sleep(25)'\"")
        {
            UseShellExecute = false
        });

        Assert.NotNull(host);
        var monitor = new SnapshotProcessMonitor();
        var targetProcessId = 0;
        var monitoring = monitor.RunAsync(async (processEvent, _) =>
        {
            if (processEvent is ProcessStartedEvent started)
            {
                if (started.ProcessName != "python.exe" || started.ParentProcessId != host.Id)
                {
                    return;
                }

                targetProcessId = started.Identity.ProcessId;
            }

            if (targetProcessId == 0 || processEvent is ProcessStoppedEvent stopped && stopped.ProcessId != targetProcessId)
            {
                return;
            }

            var notice = await tracker.Handle(processEvent);
            if (notice?.DisplayName == "python.exe")
            {
                completion.TrySetResult(notice);
            }
        }, cancellation.Token);
        try
        {
            var notice = await completion.Task.WaitAsync(TimeSpan.FromSeconds(40));
            Assert.Equal(TaskState.EndedUnknown, notice.State);
        }
        finally
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitoring);
            if (!host.HasExited) host.Kill(true);
        }
    }
}
