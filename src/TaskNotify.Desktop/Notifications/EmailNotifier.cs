using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TaskNotify.Core.Tasks;
using TaskNotify.Infrastructure.Email;

namespace TaskNotify.Desktop.Notifications;

/// <summary>
/// Background email channel. Notices are queued via <see cref="Offer"/> and a
/// single worker drains them through SMTP. Failures never surface to the caller —
/// the toast channel is unaffected. A separate <see cref="SendTestAsync"/> entry
/// point supports the "测试发送" button on the settings page (synchronous result,
/// not queued).
///
/// When <see cref="EmailSettings.Enabled"/> is false, the worker skips every
/// notice and SendTestAsync returns false without touching the network.
/// </summary>
public sealed class EmailNotifier : IDisposable
{
    private readonly EmailSettingsStore _settings;
    private readonly ILogger<EmailNotifier> _logger;
    private readonly Channel<TaskCompletionNotice> _queue;
    private readonly Task _worker;
    private readonly CancellationTokenSource _cts = new();
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly string _logPath;

    public EmailNotifier(EmailSettingsStore settings, ILogger<EmailNotifier> logger)
    {
        _settings = settings;
        _logger = logger;
        _queue = Channel.CreateBounded<TaskCompletionNotice>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logPath = Path.Combine(root, "TaskNotify", "email.log");
        _worker = Task.Run(() => RunWorkerAsync(_cts.Token));
    }

    /// <summary>Enqueues a notice for asynchronous delivery. Never throws.</summary>
    public void Offer(TaskCompletionNotice notice)
    {
        try
        {
            if (!_queue.Writer.TryWrite(notice))
            {
                _logger.LogWarning("Email queue full; dropped notice for {TaskId}.", notice.TaskId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue email for {TaskId}.", notice.TaskId);
        }
    }

    /// <summary>
    /// Sends a fixed-content test mail using the current settings. Returns true on
    /// success, false on any failure (including disabled or invalid config). Used by
    /// the settings page "测试发送" button.
    /// </summary>
    public async Task<bool> SendTestAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        if (!settings.Enabled)
        {
            _logger.LogInformation("Test send skipped: email disabled.");
            return false;
        }
        if (!HasMinimumConfig(settings))
        {
            _logger.LogWarning("Test send skipped: SMTP config incomplete.");
            return false;
        }

        var subject = $"{settings.SubjectPrefix} 测试邮件 · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}";
        var body = BuildTestBody();
        try
        {
            await SendCoreAsync(settings, subject, body, true, body, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test send failed.");
            WriteDiagnosticLog("Test send failed: " + ex);
            return false;
        }
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        await foreach (var notice in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await SendNoticeWithRetryAsync(notice, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email delivery failed for {TaskId}.", notice.TaskId);
                WriteDiagnosticLog($"Notice {notice.TaskId} ({notice.State}): {ex}");
            }
        }
    }

    private async Task SendNoticeWithRetryAsync(TaskCompletionNotice notice, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        if (!settings.Enabled || !HasMinimumConfig(settings))
        {
            return;
        }

        var subject = BuildNoticeSubject(settings, notice);
        var (html, text) = BuildNoticeBody(notice);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(SendTimeout);
                await SendCoreAsync(settings, subject, html, false, text, cts.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt == 1)
            {
                _logger.LogWarning(ex, "First attempt failed for {TaskId}; retrying after {Delay}.", notice.TaskId, RetryDelay);
                try { await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
            }
        }
    }

    private static bool HasMinimumConfig(EmailSettings s)
        => !string.IsNullOrWhiteSpace(s.SmtpHost)
            && !string.IsNullOrWhiteSpace(s.FromAddress)
            && s.ToAddresses.Count > 0;

#pragma warning disable SYSLIB0011 // System.Net.Mail is obsolete; sufficient for desktop SMTP.
    private static async Task SendCoreAsync(
        EmailSettings s,
        string subject,
        string htmlBody,
        bool bodyIsHtml,
        string textBody,
        CancellationToken cancellationToken)
    {
        using var message = new MailMessage();
        message.From = string.IsNullOrWhiteSpace(s.FromDisplayName)
            ? new MailAddress(s.FromAddress)
            : new MailAddress(s.FromAddress, s.FromDisplayName, Encoding.UTF8);

        foreach (var to in s.ToAddresses)
        {
            var trimmed = to.Trim();
            if (trimmed.Length > 0) message.To.Add(trimmed);
        }

        message.Subject = subject;
        message.SubjectEncoding = Encoding.UTF8;
        message.BodyEncoding = Encoding.UTF8;
        if (bodyIsHtml)
        {
            message.Body = htmlBody;
            message.IsBodyHtml = true;
        }
        else
        {
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, "text/html"));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(textBody, Encoding.UTF8, "text/plain"));
        }

        using var client = new SmtpClient(s.SmtpHost, s.SmtpPort)
        {
            EnableSsl = s.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = (int)SendTimeout.TotalMilliseconds,
        };

        if (!string.IsNullOrEmpty(s.SmtpUserName))
        {
            var password = EmailPasswordProtector.Decrypt(s.SmtpPasswordEncrypted);
            client.Credentials = new NetworkCredential(s.SmtpUserName, password);
        }
        else
        {
            client.UseDefaultCredentials = true;
        }

        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }
#pragma warning restore SYSLIB0011

    private static string BuildNoticeSubject(EmailSettings s, TaskCompletionNotice n)
    {
        var state = StateLabel(n.State);
        var emoji = StateEmoji(n.State);
        var name = n.DisplayName.Length > 60 ? n.DisplayName[..60] + "…" : n.DisplayName;
        return $"{s.SubjectPrefix} {emoji} {name} - {state}";
    }

    private static (string html, string text) BuildNoticeBody(TaskCompletionNotice n)
    {
        var state = StateLabel(n.State);
        var emoji = StateEmoji(n.State);
        var duration = n.Duration.TotalSeconds < 1 ? "< 1s" : n.Duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        var rows = new StringBuilder();
        AppendRow(rows, "任务", WebEscape(n.DisplayName));
        AppendRow(rows, "状态", $"{emoji} {state}");
        AppendRow(rows, "用时", WebEscape(duration));
        if (!string.IsNullOrWhiteSpace(n.ProcessName)) AppendRow(rows, "进程", WebEscape(n.ProcessName));
        if (!string.IsNullOrWhiteSpace(n.WorkingDirectory)) AppendRow(rows, "工作目录", WebEscape(n.WorkingDirectory));
        if (!string.IsNullOrWhiteSpace(n.OpenPath)) AppendRow(rows, "输出路径", WebEscape(n.OpenPath));
        if (!string.IsNullOrWhiteSpace(n.LogPath)) AppendRow(rows, "日志", WebEscape(n.LogPath));
        if (!string.IsNullOrWhiteSpace(n.ResultMessage)) AppendRow(rows, "结果", WebEscape(n.ResultMessage));
        AppendRow(rows, "时间", WebEscape(now));

        var html = $@"<!DOCTYPE html>
<html lang=""zh-CN"">
<body style=""font-family: 'Segoe UI', Arial, sans-serif; color:#1f2937; background:#f9fafb; padding:24px;"">
  <div style=""max-width:560px; margin:0 auto; background:white; border:1px solid #e5e7eb; border-radius:8px; overflow:hidden;"">
    <div style=""background:#111827; color:white; padding:16px 24px;"">
      <div style=""font-size:18px; font-weight:600;"">TaskNotify 任务通知</div>
    </div>
    <table style=""width:100%; border-collapse:collapse; font-size:14px;"">{rows}</table>
    <div style=""background:#f3f4f6; padding:12px 24px; font-size:12px; color:#6b7280;"">
      本邮件由 TaskNotify 自动发送
    </div>
  </div>
</body>
</html>";

        var text = $"TaskNotify 任务通知{Environment.NewLine}"
            + $"任务: {n.DisplayName}{Environment.NewLine}"
            + $"状态: {emoji} {state}{Environment.NewLine}"
            + $"用时: {duration}{Environment.NewLine}"
            + (string.IsNullOrWhiteSpace(n.ProcessName) ? "" : $"进程: {n.ProcessName}{Environment.NewLine}")
            + (string.IsNullOrWhiteSpace(n.WorkingDirectory) ? "" : $"工作目录: {n.WorkingDirectory}{Environment.NewLine}")
            + (string.IsNullOrWhiteSpace(n.OpenPath) ? "" : $"输出路径: {n.OpenPath}{Environment.NewLine}")
            + (string.IsNullOrWhiteSpace(n.LogPath) ? "" : $"日志: {n.LogPath}{Environment.NewLine}")
            + (string.IsNullOrWhiteSpace(n.ResultMessage) ? "" : $"结果: {n.ResultMessage}{Environment.NewLine}")
            + $"时间: {now}{Environment.NewLine}";

        return (html, text);
    }

    private static string BuildTestBody()
    {
        var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var html = $@"<!DOCTYPE html>
<html lang=""zh-CN"">
<body style=""font-family: 'Segoe UI', Arial, sans-serif; color:#1f2937; background:#f9fafb; padding:24px;"">
  <div style=""max-width:560px; margin:0 auto; background:white; border:1px solid #e5e7eb; border-radius:8px; padding:24px;"">
    <div style=""font-size:18px; font-weight:600; margin-bottom:12px;"">TaskNotify 测试邮件</div>
    <p style=""margin:0 0 8px;"">这是一封来自 TaskNotify 的测试邮件，用于验证 SMTP 配置是否正确。</p>
    <p style=""margin:0; color:#6b7280; font-size:13px;"">配置生效时间：{WebEscape(now)}</p>
  </div>
</body>
</html>";
        return html;
    }

    private static void AppendRow(StringBuilder sb, string label, string value)
    {
        sb.Append("<tr><td style=\"padding:10px 24px; border-bottom:1px solid #f3f4f6; color:#6b7280; width:120px; vertical-align:top;\">")
          .Append(WebEscape(label))
          .Append("</td><td style=\"padding:10px 24px; border-bottom:1px solid #f3f4f6; word-break:break-all;\">")
          .Append(value)
          .AppendLine("</td></tr>");
    }

    private static string WebEscape(string? s)
        => string.IsNullOrEmpty(s) ? string.Empty
            : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string StateLabel(TaskState state) => state switch
    {
        TaskState.WaitingForInput => "等待用户输入",
        TaskState.WaitingForPermission => "需要用户确认",
        TaskState.Succeeded => "执行成功",
        TaskState.Failed => "执行失败",
        TaskState.Cancelled => "已取消",
        TaskState.TimedOut => "已超时",
        TaskState.Ignored => "已忽略",
        TaskState.PossiblyCompleted => "推测完成",
        _ => "结果状态未知"
    };

    private static string StateEmoji(TaskState state) => state switch
    {
        TaskState.Succeeded => "✅",
        TaskState.Failed => "❌",
        TaskState.Cancelled => "⏹️",
        TaskState.TimedOut => "⏱️",
        TaskState.WaitingForInput => "⌨️",
        TaskState.WaitingForPermission => "🔐",
        TaskState.PossiblyCompleted => "🤔",
        _ => "❔"
    };

    private void WriteDiagnosticLog(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(_logPath, $"[{DateTimeOffset.UtcNow:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // best-effort
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(5));
        }
        catch { /* best-effort drain */ }
        _cts.Dispose();
    }
}
