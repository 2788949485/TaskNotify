using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>Unified feed — shows every recorded task regardless of state.</summary>
public sealed class TaskCenterViewModel : FilteredTasksViewModelBase
{
    public TaskCenterViewModel(TaskHistoryViewModel store) : base(store)
    {
        Title = "任务中心";
    }

    protected override bool Filter(TaskState state) => true;
}
