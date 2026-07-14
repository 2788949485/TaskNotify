using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Learning;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// Page view model that filters the shared task store by a predicate. Bound to
/// the shared <see cref="TaskHistoryViewModel"/>'s <c>Completed</c> collection.
/// Also hosts the right-click learning actions (doc chapter 24.4) — these are
/// available on every task page.
/// </summary>
public abstract partial class FilteredTasksViewModelBase : PageViewModelBase
{
    private readonly TaskHistoryViewModel _store;
    private readonly LearningActions? _learning;
    private readonly IDetectedTaskRepository? _taskRepo;
    private readonly ILogger? _logger;

    protected FilteredTasksViewModelBase(TaskHistoryViewModel store)
    {
        _store = store;
        Tasks = CollectionViewSource.GetDefaultView(store.Completed);
        Tasks.Filter = obj => obj is TaskCompletionNoticeViewModel vm && Filter(vm.State);
        store.Completed.CollectionChanged += (_, _) => Tasks.Refresh();
    }

    /// <summary>Optional injection point for the learning actions.</summary>
    protected FilteredTasksViewModelBase(
        TaskHistoryViewModel store,
        LearningActions learning,
        IDetectedTaskRepository taskRepo,
        ILogger logger) : this(store)
    {
        _learning = learning;
        _taskRepo = taskRepo;
        _logger = logger;
    }

    public ICollectionView Tasks { get; }

    public string RuntimeStatus => _store.RuntimeStatus;

    /// <summary>Selected row in the page's list. Drives the context-menu commands.</summary>
    [ObservableProperty]
    private TaskCompletionNoticeViewModel? _selectedTask;

    /// <summary>
    /// Selects the row matching <paramref name="taskId"/> if it's visible in the
    /// current filter. Used by toast click-through navigation.
    /// </summary>
    public void SelectTaskById(Guid taskId)
    {
        if (_store.FindById(taskId) is { } existing)
        {
            SelectedTask = existing;
            Tasks.MoveCurrentTo(existing);
            return;
        }

        // Fallback: hydrate from the repo if the toast points at a task no longer in the in-memory store.
        if (_taskRepo is null) return;
        try
        {
            var task = _taskRepo.FindByIdAsync(taskId).GetAwaiter().GetResult();
            if (task is null) return;
            var duration = task.EndedAt is { } ended
                ? ended - (task.StartedAt ?? task.DetectedAt)
                : TimeSpan.Zero;
            _store.Add(new TaskCompletionNotice(
                task.Id, task.DisplayName, duration, task.State,
                task.ProcessName, task.WorkingDirectory, task.OpenPath, task.LogPath, task.ResultMessage));
            if (_store.FindById(taskId) is { } hydrated)
            {
                SelectedTask = hydrated;
                Tasks.MoveCurrentTo(hydrated);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load task {TaskId} from repository for selection.", taskId);
        }
    }

    protected abstract bool Filter(TaskState state);

    [RelayCommand(CanExecute = nameof(CanLearn))]
    private async Task AlwaysRemindAsync()
    {
        if (!TryGetSelected(out var task, out var error)) { return; }
        await SafeAsync(() => _learning!.AlwaysRemindForThisKindAsync(task));
    }

    [RelayCommand(CanExecute = nameof(CanLearn))]
    private async Task NeverRemindAsync()
    {
        if (!TryGetSelected(out var task, out _)) return;
        await SafeAsync(() => _learning!.NeverRemindThisProgramAsync(task));
    }

    [RelayCommand(CanExecute = nameof(CanLearn))]
    private async Task MarkAsServiceAsync()
    {
        if (!TryGetSelected(out var task, out _)) return;
        await SafeAsync(() => _learning!.MarkAsBackgroundServiceAsync(task));
    }

    [RelayCommand(CanExecute = nameof(CanLearn))]
    private async Task MarkAsDataToolAsync()
    {
        if (!TryGetSelected(out var task, out _)) return;
        await SafeAsync(() => _learning!.MarkAsDataToolAsync(task));
    }

    [RelayCommand(CanExecute = nameof(CanLearn))]
    private async Task BumpThresholdAsync()
    {
        if (!TryGetSelected(out var task, out _)) return;
        await SafeAsync(() => _learning!.AdjustNotificationThresholdAsync(task, 60));
    }

    partial void OnSelectedTaskChanged(TaskCompletionNoticeViewModel? value)
    {
        AlwaysRemindCommand.NotifyCanExecuteChanged();
        NeverRemindCommand.NotifyCanExecuteChanged();
        MarkAsServiceCommand.NotifyCanExecuteChanged();
        MarkAsDataToolCommand.NotifyCanExecuteChanged();
        BumpThresholdCommand.NotifyCanExecuteChanged();
    }

    private bool CanLearn() => _learning is not null && SelectedTask is not null;

    private bool TryGetSelected(out DetectedTask task, out string error)
    {
        error = string.Empty;
        if (_learning is null || _taskRepo is null || SelectedTask is null)
        {
            error = "学习服务未就绪。";
            task = null!;
            return false;
        }
        var found = _taskRepo.FindByIdAsync(SelectedTask.TaskId).GetAwaiter().GetResult();
        if (found is null)
        {
            error = "找不到任务记录。";
            task = null!;
            return false;
        }
        task = found;
        return true;
    }

    private async Task SafeAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); }
        catch (Exception ex) { _logger?.LogError(ex, "Learning action failed."); }
    }
}
