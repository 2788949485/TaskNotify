using System.Drawing;
using Forms = System.Windows.Forms;
using TaskNotify.Core;

namespace TaskNotify.Desktop;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIconService(Action showWindow, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开任务中心", null, (_, _) => showWindow());
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

    public void Show(TaskCompletionNotice notice) =>
        _icon.ShowBalloonTip(5000, "任务进程已结束", $"{notice.DisplayName}\n结果状态暂时无法确认 · 用时 {notice.Duration:mm\\:ss}", Forms.ToolTipIcon.Info);

    public void Dispose() => _icon.Dispose();
}
