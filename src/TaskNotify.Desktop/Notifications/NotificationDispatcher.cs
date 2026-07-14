using Microsoft.Extensions.Logging;
using TaskNotify.Core.Notifications;
using TaskNotify.Core.Tasks;
using TaskNotify.Infrastructure.Settings;

namespace TaskNotify.Desktop.Notifications;

/// <summary>
/// Glues <see cref="NotificationMerger"/>, <see cref="NotificationCooldown"/> and
/// <see cref="TrayIconService"/> together. Notices are offered to the merger; a
/// background timer drains expired buckets and routes them through the cooldown
/// gate before they reach the tray.
/// </summary>
public sealed class NotificationDispatcher : IDisposable
{
    private readonly TrayIconService _tray;
    private readonly NotificationActionHandlers _handlers;
    private readonly NotificationMerger _merger;
    private readonly NotificationCooldown _cooldown;
    private readonly EmailNotifier? _email;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly System.Threading.Timer _drainTimer;
    private readonly TimeSpan _drainPeriod = TimeSpan.FromMilliseconds(500);

    public NotificationDispatcher(
        TrayIconService tray,
        NotificationActionHandlers handlers,
        AppSettingsStore settings,
        ILogger<NotificationDispatcher> logger,
        EmailNotifier? email = null)
    {
        _tray = tray;
        _handlers = handlers;
        _email = email;
        _logger = logger;
        var current = settings.Current;
        _merger = new(TimeSpan.FromSeconds(current.MergeBurstSeconds > 0 ? current.MergeBurstSeconds : 5));
        _cooldown = new(TimeSpan.FromSeconds(current.NotificationCooldownSeconds > 0 ? current.NotificationCooldownSeconds : 10));
        _drainTimer = new System.Threading.Timer(_ => DrainExpired(), null, _drainPeriod, _drainPeriod);
    }

    /// <summary>Entry point invoked by TaskMonitorService whenever a notice is produced.</summary>
    public void Offer(TaskCompletionNotice notice)
    {
        try
        {
            _merger.Offer(notice, DateTimeOffset.UtcNow);
            DrainExpired();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to offer notice for {TaskId}.", notice.TaskId);
        }
    }

    private void DrainExpired()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var ready = _merger.DrainExpired(now);
            foreach (var notice in ready)
            {
                if (_cooldown.TryAcquire(notice, now))
                {
                    _tray.Show(notice);
                    if (_email is not null)
                    {
                        try { _email.Offer(notice); } catch { /* best-effort */ }
                    }
                }
            }
            _cooldown.Compact(now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notification dispatcher drain failed.");
        }
    }

    public void Dispose()
    {
        _drainTimer.Dispose();
        foreach (var notice in _merger.DrainAll())
        {
            try { _tray.Show(notice); } catch { /* best-effort on shutdown */ }
            if (_email is not null)
            {
                try { _email.Offer(notice); } catch { /* best-effort */ }
            }
        }
        _email?.Dispose();
    }
}
