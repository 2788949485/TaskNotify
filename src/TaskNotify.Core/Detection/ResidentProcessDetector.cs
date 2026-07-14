using System.Text.RegularExpressions;
using TaskNotify.Core.Interfaces;

namespace TaskNotify.Core.Detection;

/// <summary>
/// A process becomes "resident" (i.e. will not trigger completion notifications)
/// when any of the conditions in doc chapter 11.3 holds. This class performs
/// only the dynamic conditions — static conditions (server/daemon keyword,
/// user-marked Resident rule) are handled by the rule engine itself.
///
/// Conditions:
/// 1. The process has been running for more than 8 hours.
/// 2. Same process name appeared on at least 4 distinct days in the last 7 days.
/// 3. The process started within 5 minutes of system boot.
/// </summary>
public sealed class ResidentProcessDetector
{
    private static readonly TimeSpan LongRunningThreshold = TimeSpan.FromHours(8);
    private static readonly TimeSpan BootWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(7);
    private const int HistoryAppearanceDays = 4;

    private readonly IDetectedTaskRepository _taskRepository;
    private readonly ISystemBootProvider _bootProvider;

    public ResidentProcessDetector(
        IDetectedTaskRepository taskRepository,
        ISystemBootProvider bootProvider)
    {
        _taskRepository = taskRepository;
        _bootProvider = bootProvider;
    }

    /// <summary>
    /// Cheap synchronous checks that don't need DB access. Called for every process
    /// candidate so must be fast.
    /// </summary>
    public ResidentCheckResult CheckFast(ProcessCandidate candidate, DateTimeOffset now)
    {
        // 1. 8-hour long-run.
        if (now - candidate.StartedAt >= LongRunningThreshold)
        {
            return new(true, "连续运行超过 8 小时");
        }

        // 2. Started near system boot.
        var boot = _bootProvider.GetBootTimeUtc();
        if (boot is { } b && candidate.StartedAt - b is { } delta && delta >= TimeSpan.Zero && delta <= BootWindow)
        {
            return new(true, "开机 5 分钟内启动");
        }

        return new(false, null);
    }

    /// <summary>
    /// Async DB-backed check: same process name appeared on at least 4 distinct days
    /// in the last 7 days. Call this only when fast checks don't already classify
    /// the process as resident (e.g. right before firing a notification).
    /// </summary>
    public async Task<ResidentCheckResult> CheckHistoryAsync(
        ProcessCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var since = now - HistoryWindow;
        var recent = await _taskRepository.FindRecentForProcessAsync(
            candidate.ProcessName,
            since,
            cancellationToken).ConfigureAwait(false);

        if (recent.Count >= HistoryAppearanceDays)
        {
            return new(true, $"过去 7 天内出现 {recent.Count} 天");
        }

        return new(false, null);
    }

    /// <summary>Convenience: run both fast and slow checks, returning first hit.</summary>
    public async Task<ResidentCheckResult> EvaluateAsync(
        ProcessCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fast = CheckFast(candidate, now);
        if (fast.IsResident) return fast;

        return await CheckHistoryAsync(candidate, now, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record ResidentCheckResult(bool IsResident, string? Reason);

/// <summary>
/// Abstraction over "when did the system last boot?" so we can fake it in tests.
/// The default implementation reads from a Windows performance counter.
/// </summary>
public interface ISystemBootProvider
{
    DateTimeOffset? GetBootTimeUtc();
}
