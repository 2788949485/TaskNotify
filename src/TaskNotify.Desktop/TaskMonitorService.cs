using System.Windows;
using TaskNotify.Core;
using TaskNotify.Integrations;
using TaskNotify.ProcessMonitor;

namespace TaskNotify.Desktop;

public sealed class TaskMonitorService
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ProcessTaskTracker _tracker = new();
    private readonly WmiProcessMonitor _monitor;
    private readonly SnapshotProcessMonitor _snapshotMonitor = new();
    private readonly IntegrationEventListener _integrationListener;
    private readonly TaskHistoryViewModel _history;
    private readonly TrayIconService _tray;
    private readonly object _snapshotSync = new();
    private Task? _wmiTask;
    private Task? _snapshotTask;
    private CancellationTokenSource? _snapshotCancellation;

    public TaskMonitorService(TaskHistoryViewModel history, TrayIconService tray)
    {
        _history = history;
        _tray = tray;
        _monitor = new(StartSnapshotFallback, StopSnapshotFallback);
        _integrationListener = new(_tracker);
        _integrationListener.OnCompletionNotice += HandleNotice;
    }

    public void Start()
    {
        _wmiTask = RunWmiAsync();
        _integrationListener.Start();
    }

    public void Stop()
    {
        _cancellation.Cancel();
        StopSnapshotFallback();
        _integrationListener.Dispose();
    }

    private async ValueTask ForwardToTracker(ProcessLifecycleEvent processEvent, CancellationToken cancellationToken)
    {
        await HandleEventAsync(processEvent, cancellationToken).ConfigureAwait(false);
    }

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

    private void HandleNotice(TaskCompletionNotice notice)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _history.Add(notice);
            _tray.Show(notice);
        });
    }

    private void StartSnapshotFallback(Exception _)
    {
        lock (_snapshotSync)
        {
            if (_snapshotCancellation is not null) return;

            _snapshotCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
            _snapshotTask = RunSnapshotAsync(_snapshotCancellation.Token);
        }
    }

    private void StopSnapshotFallback()
    {
        lock (_snapshotSync)
        {
            _snapshotCancellation?.Cancel();
            _snapshotCancellation?.Dispose();
            _snapshotCancellation = null;
            _snapshotTask = null;
        }
    }

    private async Task RunSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _snapshotMonitor.RunAsync(ForwardToTracker, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunWmiAsync()
    {
        try
        {
            await _monitor.RunAsync(ForwardToTracker, _cancellation.Token);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }
}
