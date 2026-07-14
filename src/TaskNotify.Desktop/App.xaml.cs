using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Windows.AppNotifications;
using TaskNotify.Integrations.Claude;
using TaskNotify.Integrations.Codex;
using TaskNotify.Integrations.Hermes;
using TaskNotify.Integrations.PowerShell;

namespace TaskNotify.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private MainWindow? _mainWindow;
    private TaskHistoryViewModel? _history;
    private TaskMonitorService? _monitor;
    private TrayIconService? _tray;
    private bool _notificationsRegistered;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--install-hermes", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                HermesSettingsManager.Install();
                Shutdown(0);
            }
            catch
            {
                Shutdown(1);
            }
            return;
        }

        RegisterNotifications();

        var services = new ServiceCollection()
            .AddSingleton<TaskHistoryViewModel>()
            .AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();
        _history = _services.GetRequiredService<TaskHistoryViewModel>();
        _mainWindow = _services.GetRequiredService<MainWindow>();
        _tray = new(ShowMainWindow, ExitApplication, TestNotification, InstallPowerShellIntegration, InstallClaudeIntegration, InstallCodexIntegration, InstallHermesIntegration);
        RefreshInstalledIntegrations();
        _monitor = new(_history, _tray);
        _monitor.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Stop();
        _tray?.Dispose();
        _services?.Dispose();
        if (_notificationsRegistered) AppNotificationManager.Default.Unregister();
        base.OnExit(e);
    }

    private void ShowMainWindow()
    {
        _mainWindow?.Show();
        _mainWindow?.Activate();
    }

    private void ExitApplication()
    {
        if (_mainWindow is not null) _mainWindow.AllowClose = true;
        Shutdown();
    }

    private void InstallPowerShellIntegration()
    {
        try
        {
            var changed = PowerShellProfileInstaller.Install();
            _tray?.ShowStatus(changed ? "PowerShell 集成已安装；请新开一个 WezTerm 或 PowerShell 窗口。" : "PowerShell 集成已经安装。\n");
        }
        catch (Exception)
        {
            _tray?.ShowStatus("PowerShell 集成安装失败。请检查 Profile 文件权限。");
        }
    }

    private void TestNotification()
    {
        var notice = new TaskNotify.Core.TaskCompletionNotice(
            Guid.NewGuid(), "TaskNotify 测试", TimeSpan.Zero, TaskNotify.Core.TaskState.EndedUnknown);
        _history?.Add(notice);
        _tray?.Show(notice);
    }

    private static void RefreshInstalledIntegrations()
    {
        try
        {
            if (PowerShellProfileInstaller.IsInstalled()) PowerShellProfileInstaller.Install();
            if (ClaudeSettingsManager.IsInstalled()) ClaudeSettingsManager.Install();
            if (CodexSettingsManager.IsInstalled()) CodexSettingsManager.Install();
            if (HermesSettingsManager.IsInstalled()) HermesSettingsManager.Install();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError($"Integration refresh failed: {exception.GetType().Name}");
        }
    }

    private void InstallClaudeIntegration()
    {
        try
        {
            var changed = ClaudeSettingsManager.Install();
            _tray?.ShowStatus(changed
                ? "Claude Code 集成已安装；后续会自动通知完成、失败和等待确认。"
                : "Claude Code 集成已经安装。");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError($"Claude integration installation failed: {exception.GetType().Name}");
            _tray?.ShowStatus("Claude Code 集成安装失败，请检查 settings.json 或 Node.js。");
        }
    }

    private void InstallCodexIntegration()
    {
        try
        {
            var changed = CodexSettingsManager.Install();
            _tray?.ShowStatus(changed
                ? "Codex 集成已安装；请在 Codex 中运行 /hooks 并信任 TaskNotify Hook。"
                : "Codex 集成已经安装。");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError($"Codex integration installation failed: {exception.GetType().Name}");
            _tray?.ShowStatus("Codex 集成安装失败，请检查 hooks.json 或 Node.js。");
        }
    }

    private void InstallHermesIntegration()
    {
        try
        {
            var changed = HermesSettingsManager.Install();
            _tray?.ShowStatus(changed
                ? "Hermes Agent 集成已安装；请重启 Hermes 并批准 TaskNotify Hook。"
                : "Hermes Agent 集成已经安装。");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError($"Hermes integration installation failed: {exception.GetType().Name}");
            _tray?.ShowStatus("Hermes Agent 集成安装失败；不会覆盖已有 hooks。");
        }
    }

    /// <summary>
    /// 从通知双击进入：打开主窗口并选中对应任务。
    /// </summary>
    internal void OnNoticeClicked(Guid taskId)
    {
        ShowMainWindow();
        _mainWindow?.SelectTaskById(taskId);
    }

    private void RegisterNotifications()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, args) =>
            {
                var taskId = ParseTaskId(args.Argument);
                if (taskId is not null)
                {
                    Dispatcher.BeginInvoke(() => OnNoticeClicked(taskId.Value));
                }
            };
            AppNotificationManager.Default.Register();
            _notificationsRegistered = true;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError($"TaskNotify notification registration failed: {exception.GetType().Name}");
        }
    }

    private static Guid? ParseTaskId(string arguments)
    {
        foreach (var argument in arguments.Split('&'))
        {
            var pair = argument.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "taskId" && Guid.TryParse(pair[1], out var taskId))
            {
                return taskId;
            }
        }

        return null;
    }
}
