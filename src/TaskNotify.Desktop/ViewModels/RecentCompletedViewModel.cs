using Microsoft.Extensions.Logging;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Learning;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>Only tasks that have reached a terminal state (success/fail/cancelled/…).</summary>
public sealed class RecentCompletedViewModel : FilteredTasksViewModelBase
{
    public RecentCompletedViewModel(
        TaskHistoryViewModel store,
        LearningActions learning,
        IDetectedTaskRepository taskRepo,
        ILogger<RecentCompletedViewModel> logger) : base(store, learning, taskRepo, logger)
    {
        Title = "最近完成";
    }

    protected override bool Filter(TaskState state) => TaskStateMachine.IsTerminal(state);
}
