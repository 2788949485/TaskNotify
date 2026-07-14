using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Learning;
using TaskNotify.Core.Performance;
using TaskNotify.Core.Recovery;
using TaskNotify.Core.Tasks;
using TaskNotify.Infrastructure;
using TaskNotify.Infrastructure.Settings;
using TaskNotify.Infrastructure.SystemInfo;
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
        RegisterGlobalExceptionHandlers();
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

        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddTaskNotifyFileLogger());

        services.AddSingleton<SqliteDatabase>(sp => new SqliteDatabase(
            customPath: null,
            logger: sp.GetRequiredService<ILogger<SqliteDatabase>>(),
            loggerFactory: sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IDetectedTaskRepository, Infrastructure.Repositories.DetectedTaskRepository>();
        services.AddSingleton<IProcessEventRepository, Infrastructure.Repositories.ProcessEventRepository>();
        services.AddSingleton<IDetectionRuleRepository, Infrastructure.Repositories.DetectionRuleRepository>();
        services.AddSingleton<IIntegrationRepository, Infrastructure.Repositories.IntegrationRepository>();
        services.AddSingleton<JsonSettingsStore>();
        services.AddSingleton<AppSettingsStore>();
        services.AddSingleton<ISystemBootProvider, SystemBootProvider>();
        services.AddSingleton<ResidentProcessDetector>();
        services.AddSingleton<LearningActions>();
        services.AddSingleton<CapacityGuard>();
        services.AddSingleton<Services.NavigationService>();
        services.AddSingleton<ViewModels.MainViewModel>();
        services.AddSingleton<ViewModels.TaskCenterViewModel>();
        services.AddSingleton<ViewModels.RunningTasksViewModel>();
        services.AddSingleton<ViewModels.RecentCompletedViewModel>();
        services.AddSingleton<ViewModels.DetectionSettingsViewModel>();
        services.AddSingleton<ViewModels.ProgramRulesViewModel>();
        services.AddSingleton<ViewModels.IntegrationManagerViewModel>();
        services.AddSingleton<ViewModels.NotificationSettingsViewModel>();
        services.AddSingleton<ViewModels.PrivacySettingsViewModel>();
        services.AddSingleton<ViewModels.SystemSettingsViewModel>();
        services.AddSingleton<TaskRecoveryService>();
        services.AddSingleton<TaskHistoryViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        // Initialize SQLite schema (idempotent). Failure is non-fatal; repositories will no-op.
        try
        {
            var database = _services.GetRequiredService<SqliteDatabase>();
            database.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _services.GetRequiredService<ILogger<App>>().LogError(exception, "Failed to initialize SQLite database.");
        }

        // Recover tasks left Running by a previous crash (doc chapter 28.2). Non-fatal on failure.
        try
        {
            var recovery = _services.GetRequiredService<TaskRecoveryService>();
            var recovered = recovery.RecoverAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (recovered > 0)
            {
                _services.GetRequiredService<ILogger<App>>()
                    .LogInformation("Recovered {Count} stale task(s) as EndedUnknown.", recovered);
            }
        }
        catch (Exception exception)
        {
            _services.GetRequiredService<ILogger<App>>().LogError(exception, "Task recovery scan failed; continuing.");
        }

        _history = _services.GetRequiredService<TaskHistoryViewModel>();
        _mainWindow = _services.GetRequiredService<MainWindow>();
        _tray = new(ShowMainWindow, ExitApplication, TestNotification, InstallPowerShellIntegration, InstallClaudeIntegration, InstallCodexIntegration, InstallHermesIntegration);
        RefreshInstalledIntegrations();
        var taskRepo = _services.GetRequiredService<IDetectedTaskRepository>();
        var eventRepo = _services.GetRequiredService<IProcessEventRepository>();
        var capacity = _services.GetRequiredService<CapacityGuard>();
        var settings = _services.GetRequiredService<AppSettingsStore>();
        var residentDetector = _services.GetRequiredService<ResidentProcessDetector>();
        _monitor = new(_history, _tray, taskRepo, eventRepo, capacity, settings, residentDetector);
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
        var notice = new TaskCompletionNotice(
            Guid.NewGuid(), "TaskNotify 测试", TimeSpan.Zero, TaskState.EndedUnknown);
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

    /// <summary>
    /// Writes unhandled exceptions to %LOCALAPPDATA%\TaskNotify\crash.log so we can
    /// diagnose startup crashes that disappear with the window. Attached before any
    /// DI plumbing so it survives failures in container build / window construction.
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, "TaskNotify");
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "crash.log");

        void Write(Exception ex, string source)
        {
            try
            {
                var message = $"[{DateTimeOffset.UtcNow:O}] [{source}]{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";
                File.AppendAllText(logPath, message);
            }
            catch
            {
                // Last-resort: swallow. We can't let exception handling itself crash the app.
            }
            System.Windows.MessageBox.Show(
                $"{source}: {ex.GetType().Name}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}详细信息已写入：{logPath}",
                "TaskNotify 启动失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Write(args.Exception, "Dispatcher");
            args.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Write(ex, "AppDomain");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write(args.Exception, "UnobservedTask");
            args.SetObserved();
        };
    }
}
