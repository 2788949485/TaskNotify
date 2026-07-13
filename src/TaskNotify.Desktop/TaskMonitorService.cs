using System.Windows;
using TaskNotify.Core;
using TaskNotify.ProcessMonitor;

namespace TaskNotify.Desktop;

public sealed class TaskMonitorService
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ProcessTaskTracker _tracker = new();
    private readonly WmiProcessMonitor _monitor = new();
    private readonly TaskHistoryViewModel _history;
    private readonly TrayIconService _tray;
    private Task? _runTask;

    public TaskMonitorService(TaskHistoryViewModel history, TrayIconService tray)
    {
        _history = history;
        _tray = tray;
    }

    public void Start() => _runTask = _monitor.RunAsync(HandleEventAsync, _cancellation.Token);

    public void Stop() => _cancellation.Cancel();

    private async ValueTask HandleEventAsync(ProcessLifecycleEvent processEvent, CancellationToken cancellationToken)
    {
        var notice = _tracker.Handle(processEvent);
        if (notice is null) return;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _history.Add(notice);
            _tray.Show(notice);
        }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken);
    }
}
