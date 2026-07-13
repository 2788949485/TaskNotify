using System.Windows;
using TaskNotify.Core;
using TaskNotify.ProcessMonitor;

namespace TaskNotify.Desktop;

public sealed class TaskMonitorService
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ProcessTaskTracker _tracker = new();
    private readonly WmiProcessMonitor _monitor;
    private readonly SnapshotProcessMonitor _snapshotMonitor = new();
    private readonly TaskHistoryViewModel _history;
    private readonly TrayIconService _tray;
    private Task? _wmiTask;
    private Task? _snapshotTask;

    public TaskMonitorService(TaskHistoryViewModel history, TrayIconService tray)
    {
        _history = history;
        _tray = tray;
        _monitor = new();
    }

    public void Start()
    {
        _snapshotTask = _snapshotMonitor.RunAsync(HandleEventAsync, _cancellation.Token);
        _wmiTask = RunWmiAsync();
    }

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

    private async Task RunWmiAsync()
    {
        try
        {
            await _monitor.RunAsync(HandleEventAsync, _cancellation.Token);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }
}
