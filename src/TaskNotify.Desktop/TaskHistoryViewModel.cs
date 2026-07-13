using System.Collections.ObjectModel;
using TaskNotify.Core;

namespace TaskNotify.Desktop;

public sealed class TaskHistoryViewModel
{
    public ObservableCollection<TaskCompletionNoticeViewModel> Completed { get; } = [];

    public void Add(TaskCompletionNotice notice) =>
        Completed.Insert(0, new(notice.DisplayName, $"任务进程已结束，结果状态暂时无法确认 · 用时 {notice.Duration:mm\\:ss}"));
}

public sealed record TaskCompletionNoticeViewModel(string DisplayName, string Summary);
