using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop.Notifications;

/// <summary>
/// Defines the seven notification actions (doc chapter 23.1). Each maps to a
/// toast button label + a string sent back to <c>App.xaml.cs</c> via the
/// notification argument stream.
/// </summary>
public enum NotificationAction
{
    Open,             // 默认：通知整体点击 / 双击托盘
    OpenProject,
    OpenOutput,
    ViewLog,
    CopyError,
    IgnoreProgram,
    AlwaysRemind,
    Later
}

/// <summary>
/// Picks a subset of the seven buttons based on the task's terminal state. Failed
/// tasks surface "copy error", succeeded tasks surface "open output", speculative
/// PossiblyCompleted only offers open + later. Reduces notification clutter.
/// </summary>
public static class NotificationButtonSet
{
    public static IReadOnlyList<NotificationAction> For(TaskState state) => state switch
    {
        TaskState.Failed => [NotificationAction.OpenProject, NotificationAction.ViewLog, NotificationAction.CopyError, NotificationAction.AlwaysRemind, NotificationAction.IgnoreProgram, NotificationAction.Later],
        TaskState.WaitingForPermission or TaskState.WaitingForInput => [NotificationAction.OpenProject, NotificationAction.Later],
        TaskState.Succeeded => [NotificationAction.OpenProject, NotificationAction.OpenOutput, NotificationAction.ViewLog, NotificationAction.IgnoreProgram, NotificationAction.Later],
        TaskState.PossiblyCompleted => [NotificationAction.OpenProject, NotificationAction.OpenOutput, NotificationAction.Later],
        _ => [NotificationAction.OpenProject, NotificationAction.Later]
    };

    public static string Label(NotificationAction action) => action switch
    {
        NotificationAction.OpenProject => "打开项目",
        NotificationAction.OpenOutput => "打开输出目录",
        NotificationAction.ViewLog => "查看日志",
        NotificationAction.CopyError => "复制错误",
        NotificationAction.IgnoreProgram => "忽略此程序",
        NotificationAction.AlwaysRemind => "以后总是提醒",
        NotificationAction.Later => "稍后",
        _ => "打开"
    };
}
