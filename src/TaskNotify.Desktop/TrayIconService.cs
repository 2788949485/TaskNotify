using System.Drawing;
using System.Security.Principal;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Forms = System.Windows.Forms;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly bool _useAppNotifications = !IsElevated();

    public TrayIconService(
        Action showWindow,
        Action exit,
        Action testNotification,
        Action installPowerShellIntegration,
        Action installClaudeIntegration,
        Action installCodexIntegration,
        Action installHermesIntegration)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开任务中心", null, (_, _) => showWindow());
        menu.Items.Add("发送测试通知", null, (_, _) => testNotification());
        menu.Items.Add("安装 PowerShell / WezTerm 集成", null, (_, _) => installPowerShellIntegration());
        menu.Items.Add("安装 Claude Code 集成", null, (_, _) => installClaudeIntegration());
        menu.Items.Add("安装 Codex 集成", null, (_, _) => installCodexIntegration());
        menu.Items.Add("安装 Hermes Agent 集成", null, (_, _) => installHermesIntegration());
        menu.Items.Add("退出", null, (_, _) => exit());
        _icon = new()
        {
            Icon = SystemIcons.Information,
            Text = "TaskNotify 正在监控任务",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => showWindow();
    }

    public void Show(TaskCompletionNotice notice)
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
            _ => "结果状态暂时无法确认"
        };

        var title = notice.State is TaskState.WaitingForInput or TaskState.WaitingForPermission
            ? $"{notice.DisplayName} 需要你处理"
            : $"{notice.DisplayName} 已结束";
        var body = $"用时 {notice.Duration:mm\\:ss} · {stateText}";

        if (_useAppNotifications)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddArgument("action", "open")
                    .AddArgument("taskId", notice.TaskId.ToString("D"))
                    .AddText(title)
                    .AddText(body)
                    .BuildNotification();
                AppNotificationManager.Default.Show(notification);
                return;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceWarning($"App notification failed; using tray fallback: {exception.GetType().Name}");
            }
        }

        _icon.ShowBalloonTip(5000, title, body, Forms.ToolTipIcon.Info);
    }

    public void ShowStatus(string message) =>
        _icon.ShowBalloonTip(3000, "TaskNotify", message, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Dispose();
    }

    private static bool IsElevated() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}
