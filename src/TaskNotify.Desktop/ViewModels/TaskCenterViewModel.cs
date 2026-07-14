using Microsoft.Extensions.Logging;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Learning;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>Unified feed — shows every recorded task regardless of state.</summary>
public sealed class TaskCenterViewModel : FilteredTasksViewModelBase
{
    public TaskCenterViewModel(
        TaskHistoryViewModel store,
        LearningActions learning,
        IDetectedTaskRepository taskRepo,
        ILogger<TaskCenterViewModel> logger) : base(store, learning, taskRepo, logger)
    {
        Title = "任务中心";
    }

    protected override bool Filter(TaskState state) => true;
}
