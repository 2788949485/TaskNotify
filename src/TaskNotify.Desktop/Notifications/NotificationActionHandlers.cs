using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Learning;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Desktop.Notifications;

/// <summary>
/// Dispatches notification button clicks to the right side effect (doc chapter 23).
/// Stateful services (repositories / learning actions) are resolved lazily from the
/// DI root so the handler can be constructed before the container is fully built.
/// </summary>
public sealed class NotificationActionHandlers
{
    private readonly IServiceProvider _services;
    private readonly ILogger<NotificationActionHandlers> _logger;

    public NotificationActionHandlers(IServiceProvider services, ILogger<NotificationActionHandlers> logger)
    {
        _services = services;
        _logger = logger;
    }

    public void Dispatch(NotificationAction action, Guid taskId)
    {
        try
        {
            switch (action)
            {
                case NotificationAction.Open:
                    OpenMainWindow(taskId);
                    break;
                case NotificationAction.OpenProject:
                    ExecuteOnTask(taskId, t => ShellOpen(t.WorkingDirectory));
                    break;
                case NotificationAction.OpenOutput:
                    ExecuteOnTask(taskId, t => ShellOpen(t.OpenPath ?? t.WorkingDirectory));
                    break;
                case NotificationAction.ViewLog:
                    ExecuteOnTask(taskId, t => ShellOpen(t.LogPath));
                    break;
                case NotificationAction.CopyError:
                    ExecuteOnTask(taskId, t => CopyToClipboard(t.ResultMessage ?? t.DisplayName));
                    break;
                case NotificationAction.IgnoreProgram:
                    Learn(taskId, (la, t) => la.NeverRemindThisProgramAsync(t));
                    break;
                case NotificationAction.AlwaysRemind:
                    Learn(taskId, (la, t) => la.AlwaysRemindForThisKindAsync(t));
                    break;
                case NotificationAction.Later:
                    // Dismiss — no action needed, toast already closed.
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch notification action {Action}.", action);
        }
    }

    private static void ShellOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
            {
                StartHelper("explorer.exe", $"\"{Path.GetDirectoryName(path)}\"");
            }
            else if (Directory.Exists(path))
            {
                StartHelper("explorer.exe", $"\"{path}\"");
            }
        }
        catch { /* best-effort */ }
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try { System.Windows.Clipboard.SetText(text); } catch { /* ignore */ }
                }));
            }
        }
        catch { /* ignore */ }
    }

    private void OpenMainWindow(Guid taskId)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.OnNoticeClicked(taskId);
        }
    }

    private void ExecuteOnTask(Guid taskId, Action<DetectedTask> action)
    {
        var repo = _services.GetService(typeof(IDetectedTaskRepository)) as IDetectedTaskRepository;
        if (repo is null) return;
        var task = repo.FindByIdAsync(taskId).GetAwaiter().GetResult();
        if (task is null)
        {
            _logger.LogWarning("Notification action referenced unknown task {TaskId}.", taskId);
            return;
        }
        action(task);
    }

    private void Learn(Guid taskId, Func<LearningActions, DetectedTask, Task> action)
    {
        var repo = _services.GetService(typeof(IDetectedTaskRepository)) as IDetectedTaskRepository;
        var learning = _services.GetService(typeof(LearningActions)) as LearningActions;
        if (repo is null || learning is null) return;
        var task = repo.FindByIdAsync(taskId).GetAwaiter().GetResult();
        if (task is null) return;
        action(learning, task).GetAwaiter().GetResult();
    }

    private static void StartHelper(string fileName, string arguments)
    {
        var info = new ProcessStartInfo(fileName, arguments) { UseShellExecute = true };
        Process.Start(info);
    }
}
