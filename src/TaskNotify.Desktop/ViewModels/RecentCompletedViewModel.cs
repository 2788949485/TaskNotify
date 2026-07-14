using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>Only tasks that have reached a terminal state (success/fail/cancelled/…).</summary>
public sealed class RecentCompletedViewModel : FilteredTasksViewModelBase
{
    public RecentCompletedViewModel(TaskHistoryViewModel store) : base(store)
    {
        Title = "最近完成";
    }

    protected override bool Filter(TaskState state) => TaskStateMachine.IsTerminal(state);
}
