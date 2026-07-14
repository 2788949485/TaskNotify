using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop;

/// <summary>
/// In-memory observable store for task completion notices. Serves as the single
/// source of truth that page view models filter and project into their own views.
/// </summary>
public sealed class TaskHistoryViewModel
{
    public string RuntimeStatus { get; } = $"运行文件：{Environment.ProcessPath}";

    public ObservableCollection<TaskCompletionNoticeViewModel> Completed { get; } = [];

    public void Add(TaskCompletionNotice notice)
    {
        var vm = new TaskCompletionNoticeViewModel(
            notice.TaskId,
            notice.DisplayName,
            $"用时 {notice.Duration:mm\\:ss} · {GetStateText(notice.State)}",
            notice.State);
        // Marshal to UI thread — Add can be called from the monitor's dispatcher post
        // but also from test/notification paths that aren't on the UI thread.
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => Completed.Insert(0, vm)));
        }
        else
        {
            Completed.Insert(0, vm);
        }
    }

    private static string GetStateText(TaskState state) => state switch
    {
        TaskState.WaitingForInput => "等待用户输入",
        TaskState.WaitingForPermission => "需要用户确认",
        TaskState.Succeeded => "执行成功",
        TaskState.Failed => "执行失败",
        TaskState.Cancelled => "已取消",
        TaskState.TimedOut => "已超时",
        TaskState.Ignored => "已忽略",
        _ => "结果状态暂时无法确认"
    };

    /// <summary>
    /// 根据 TaskId 查找并返回对应的 ViewModel，用于通知双击定位。
    /// </summary>
    public TaskCompletionNoticeViewModel? FindById(Guid taskId)
    {
        foreach (var vm in Completed)
        {
            if (vm.TaskId == taskId)
            {
                return vm;
            }
        }
        return null;
    }
}

public sealed record TaskCompletionNoticeViewModel(
    Guid TaskId,
    string DisplayName,
    string Summary,
    TaskState State);
