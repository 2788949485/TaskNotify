using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Tasks;

namespace TaskNotify.Core.Learning;

/// <summary>
/// Five user learning operations (doc chapter 24.4). Each takes a task the user
/// has just been notified about and persists a user-created rule (or settings
/// override) so future similar tasks get handled the way the user wants.
///
/// All operations are idempotent on <c>RuleName</c>: re-invoking on the same
/// program updates the existing rule instead of inserting duplicates.
/// </summary>
public sealed class LearningActions
{
    private readonly IDetectionRuleRepository _ruleRepository;
    private readonly IUserSettingsOverrides _settingsOverrides;
    private readonly ILogger<LearningActions> _logger;

    public LearningActions(
        IDetectionRuleRepository ruleRepository,
        IUserSettingsOverrides settingsOverrides,
        ILogger<LearningActions> logger)
    {
        _ruleRepository = ruleRepository;
        _settingsOverrides = settingsOverrides;
        _logger = logger;
    }

    /// <summary>
    /// Suppress all notifications for this program. The task is still recorded
    /// (so it shows up in history) but no toast fires.
    /// </summary>
    public Task NeverRemindThisProgramAsync(DetectedTask task, CancellationToken cancellationToken = default)
        => SaveRuleAsync(task, RuleAction.Ignore, "用户忽略此程序");

    /// <summary>
    /// Always notify on completion regardless of probability score or duration.
    /// Useful for tasks the user cares about that fall below the default threshold.
    /// </summary>
    public Task AlwaysRemindForThisKindAsync(DetectedTask task, CancellationToken cancellationToken = default)
        => SaveRuleAsync(task, RuleAction.AlwaysNotify, "用户始终提醒");

    /// <summary>
    /// Mark the program as a long-running service (e.g. a dev server). The task
    /// is still tracked but notifications are suppressed permanently.
    /// </summary>
    public Task MarkAsBackgroundServiceAsync(DetectedTask task, CancellationToken cancellationToken = default)
        => SaveRuleAsync(task, RuleAction.Resident, "用户标记为常驻服务");

    /// <summary>
    /// Mark the program as a data tool (Excel, Tableau, Power BI, …). No
    /// completion notification — these don't represent "tasks" in the user's
    /// mental model. Uses a curated regex rather than just the process name so
    /// all common data tools fall under one rule.
    /// </summary>
    public Task MarkAsDataToolAsync(DetectedTask task, CancellationToken cancellationToken = default)
    {
        var rule = new DetectionRule(
            Name: "用户标记为数据工具",
            Action: RuleAction.Ignore,
            ProcessNamePattern: @"^(excel|tableau|powerbi|msaccess|ssms|isql|sqlworkbench)\.exe$",
            IsUserCreated: true);
        return PersistAsync(rule, CancellationToken.None);
    }

    /// <summary>
    /// Override the minimum-running duration before a notification fires for this
    /// process. <paramref name="seconds"/> of 0 resets to the built-in default.
    /// </summary>
    public Task AdjustNotificationThresholdAsync(
        DetectedTask task,
        int seconds,
        CancellationToken cancellationToken = default)
    {
        if (task.ProcessName is null)
        {
            _logger.LogWarning("AdjustNotificationThreshold called with task that has no ProcessName.");
            return Task.CompletedTask;
        }

        _settingsOverrides.MutateThreshold(task.ProcessName, seconds);
        _logger.LogInformation("Adjusted notification threshold for {Process} to {Seconds}s.",
            task.ProcessName, seconds);
        return Task.CompletedTask;
    }

    private Task SaveRuleAsync(DetectedTask task, RuleAction action, string displayName)
    {
        if (task.ProcessName is null)
        {
            _logger.LogWarning("{Action} called with task that has no ProcessName.", action);
            return Task.CompletedTask;
        }

        var rule = new DetectionRule(
            Name: RuleNameFor(task.ProcessName, action),
            Action: action,
            ProcessNamePattern: AnchorPattern(task.ProcessName),
            IsUserCreated: true);
        return PersistAsync(rule, CancellationToken.None);
    }

    private async Task PersistAsync(DetectionRule rule, CancellationToken cancellationToken)
    {
        try
        {
            await _ruleRepository.UpsertUserRuleByNameAsync(rule, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to persist learning rule {Name}.", rule.Name);
        }
    }

    /// <summary>Builds a deterministic rule name so the same (process, action) pair is upsertable.</summary>
    private static string RuleNameFor(string processName, RuleAction action) =>
        $"user:{processName.ToLowerInvariant()}:{action}";

    /// <summary>
    /// Anchors and escapes a literal process name so the regex matches the whole
    /// token (e.g. "python.exe" → "^python\.exe$"), preventing "python.exe" rules
    /// from accidentally matching "mypython.exe".
    /// </summary>
    private static string AnchorPattern(string processName) =>
        "^" + Regex.Escape(processName) + "$";
}
