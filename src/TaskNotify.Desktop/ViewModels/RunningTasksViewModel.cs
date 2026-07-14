using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>Only waiting-for-input / waiting-for-permission tasks (i.e. needs user).</summary>
public sealed class RunningTasksViewModel : FilteredTasksViewModelBase
{
    public RunningTasksViewModel(TaskHistoryViewModel store) : base(store)
    {
        Title = "正在运行";
    }

    protected override bool Filter(TaskState state) =>
        state is TaskState.WaitingForInput or TaskState.WaitingForPermission;
}
