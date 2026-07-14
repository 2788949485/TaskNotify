using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop.Notifications;

/// <summary>
/// Builds the <see cref="AppNotification"/> payload for a completion notice,
/// including the state-appropriate button set (doc chapter 23.1).
/// </summary>
public static class NotificationBuilder
{
    public static AppNotification Build(TaskCompletionNotice notice)
    {
        var (title, body) = FormatText(notice);

        var builder = new AppNotificationBuilder()
            .AddArgument("action", NotificationAction.Open.ToString())
            .AddArgument("taskId", notice.TaskId.ToString("D"))
            .AddText(title)
            .AddText(body);

        foreach (var action in NotificationButtonSet.For(notice.State))
        {
            var button = new AppNotificationButton(NotificationButtonSet.Label(action))
                .AddArgument("action", action.ToString())
                .AddArgument("taskId", notice.TaskId.ToString("D"));
            builder.AddButton(button);
        }

        return builder.BuildNotification();
    }

    private static (string Title, string Body) FormatText(TaskCompletionNotice notice)
    {
        var stateText = notice.State switch
        {
            TaskState.WaitingForInput => "等待用户输入",
            TaskState.WaitingForPermission => "需要用户确认",
            TaskState.Succeeded => "执行成功",
            TaskState.Failed => "执行失败",
            TaskState.Cancelled => "已取消",
            TaskState.TimedOut => "已超时",
            TaskState.Ignored => "已忽略",
            TaskState.PossiblyCompleted => "可能已完成（推测）",
            _ => "结果状态暂时无法确认"
        };

        var title = notice.State is TaskState.WaitingForInput or TaskState.WaitingForPermission
            ? $"{notice.DisplayName} 需要你处理"
            : $"{notice.DisplayName} 已结束";

        var body = $"用时 {notice.Duration:mm\\:ss} · {stateText}";
        return (title, body);
    }
}
