using System.Windows;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Events;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Performance;
using TaskNotify.Core.Tasks;
using TaskNotify.Infrastructure.Settings;
using TaskNotify.Integrations;
using TaskNotify.ProcessMonitor;

namespace TaskNotify.Desktop;

public sealed class TaskMonitorService
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ProcessTaskTracker _tracker;
    private readonly WmiProcessMonitor _monitor;
    private readonly SnapshotProcessMonitor _snapshotMonitor;
    private readonly IntegrationEventListener _integrationListener;
    private readonly TaskHistoryViewModel _history;
    private readonly TrayIconService _tray;
    private readonly object _snapshotSync = new();
    private Task? _wmiTask;
    private Task? _snapshotTask;
    private CancellationTokenSource? _snapshotCancellation;

    /// <summary>
    /// When set, every produced notice is forwarded here instead of being shown
    /// directly. The dispatcher applies merge + cooldown then routes to the tray.
    /// When null (tests, or no dispatcher wired), falls back to direct <see cref="TrayIconService.Show"/>.
    /// </summary>
    public Action<TaskCompletionNotice>? NoticeDispatched { get; set; }

    public TaskMonitorService(
        TaskHistoryViewModel history,
        TrayIconService tray,
        IDetectedTaskRepository taskRepository,
        IProcessEventRepository eventRepository,
        CapacityGuard capacityGuard,
        AppSettingsStore settingsStore,
        ResidentProcessDetector residentDetector)
    {
        _history = history;
        _tray = tray;
        var current = settingsStore.Current;
        var mode = current.DetectionMode;
        _tracker = new(taskRepository, eventRepository, capacityGuard, mode, residentDetector, current.NotificationThresholdsSeconds);
        _snapshotMonitor = new(mode);
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
        _tracker.Dispose();
    }

    private async ValueTask ForwardToTracker(ProcessLifecycleEvent processEvent, CancellationToken cancellationToken)
    {
        await HandleEventAsync(processEvent, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleEventAsync(ProcessLifecycleEvent processEvent, CancellationToken cancellationToken)
    {
        var notice = await _tracker.Handle(processEvent, cancellationToken).ConfigureAwait(false);
        if (notice is null) return;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _history.Add(notice);
            Dispatch(notice);
        }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken);
    }

    private void HandleNotice(TaskCompletionNotice notice)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _history.Add(notice);
            Dispatch(notice);
        });
    }

    private void Dispatch(TaskCompletionNotice notice)
    {
        if (NoticeDispatched is { } sink) sink(notice);
        else _tray.Show(notice);
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
