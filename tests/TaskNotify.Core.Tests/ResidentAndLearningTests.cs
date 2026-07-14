using Microsoft.Extensions.Logging.Abstractions;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Interfaces;
using TaskNotify.Core.Learning;
using TaskNotify.Core.Tasks;
using Xunit;

namespace TaskNotify.Core.Tests;

/// <summary>
/// Phase 3.4: verifies resident-detection conditions and the 5 learning actions.
/// </summary>
public sealed class ResidentProcessDetectorTests
{
    [Fact]
    public void Process_running_over_eight_hours_is_resident()
    {
        var repo = new FakeRepo();
        var boot = new FixedBootProvider(DateTimeOffset.UtcNow.AddHours(-1));
        var detector = new ResidentProcessDetector(repo, boot);

        var candidate = new ProcessCandidate(
            1,
            DateTimeOffset.UtcNow.AddHours(-9),
            "python.exe",
            CommandLine: "python dev_server.py");

        var result = detector.CheckFast(candidate, DateTimeOffset.UtcNow);
        Assert.True(result.IsResident);
        Assert.Contains("8 小时", result.Reason);
    }

    [Fact]
    public void Process_started_within_five_minutes_of_boot_is_resident()
    {
        var bootTime = DateTimeOffset.UtcNow.AddMinutes(-30);
        var repo = new FakeRepo();
        var boot = new FixedBootProvider(bootTime);
        var detector = new ResidentProcessDetector(repo, boot);

        var candidate = new ProcessCandidate(
            1,
            bootTime.AddMinutes(2),
            "myagent.exe",
            CommandLine: "myagent --watch");

        var result = detector.CheckFast(candidate, DateTimeOffset.UtcNow);
        Assert.True(result.IsResident);
        Assert.Contains("开机", result.Reason);
    }

    [Fact]
    public void Short_lived_user_process_is_not_resident_via_fast_path()
    {
        var repo = new FakeRepo();
        var boot = new FixedBootProvider(DateTimeOffset.UtcNow.AddHours(-2));
        var detector = new ResidentProcessDetector(repo, boot);

        var candidate = new ProcessCandidate(
            1,
            DateTimeOffset.UtcNow.AddSeconds(-30),
            "python.exe",
            CommandLine: "python train.py");

        var result = detector.CheckFast(candidate, DateTimeOffset.UtcNow);
        Assert.False(result.IsResident);
    }

    [Fact]
    public async Task Process_that_ran_on_four_days_in_last_week_is_resident_via_history()
    {
        var repo = new FakeRepo();
        var boot = new FixedBootProvider(DateTimeOffset.UtcNow.AddHours(-2));
        var detector = new ResidentProcessDetector(repo, boot);

        var procName = "build.exe";
        var now = DateTimeOffset.UtcNow;
        foreach (var day in Enumerable.Range(0, 4).Select(d => now.AddDays(-d - 1)))
        {
            repo.AddDay(procName, day);
        }

        var candidate = new ProcessCandidate(1, now.AddSeconds(-30), procName, CommandLine: "build.exe");
        var result = await detector.CheckHistoryAsync(candidate, now, CancellationToken.None);

        Assert.True(result.IsResident);
        Assert.Contains("7 天", result.Reason);
    }

    [Fact]
    public async Task Process_with_no_history_is_not_resident_via_history()
    {
        var repo = new FakeRepo();
        var boot = new FixedBootProvider(DateTimeOffset.UtcNow.AddHours(-2));
        var detector = new ResidentProcessDetector(repo, boot);

        var candidate = new ProcessCandidate(1, DateTimeOffset.UtcNow.AddSeconds(-30), "rare.exe", CommandLine: "rare");
        var result = await detector.CheckHistoryAsync(candidate, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result.IsResident);
    }

    private sealed class FixedBootProvider : ISystemBootProvider
    {
        private readonly DateTimeOffset? _boot;
        public FixedBootProvider(DateTimeOffset? boot) => _boot = boot;
        public DateTimeOffset? GetBootTimeUtc() => _boot;
    }

    private sealed class FakeRepo : IDetectedTaskRepository
    {
        private readonly Dictionary<string, HashSet<DateTimeOffset>> _days = new(StringComparer.OrdinalIgnoreCase);

        public void AddDay(string proc, DateTimeOffset day)
        {
            var key = proc.ToLowerInvariant();
            if (!_days.TryGetValue(key, out var set))
            {
                set = new();
                _days[key] = set;
            }
            set.Add(new DateTimeOffset(day.UtcDateTime.Date, TimeSpan.Zero));
        }

        public Task<IReadOnlyList<DateTimeOffset>> FindRecentForProcessAsync(string processName, DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            if (!_days.TryGetValue(processName, out var set)) return Task.FromResult<IReadOnlyList<DateTimeOffset>>(Array.Empty<DateTimeOffset>());
            var filtered = set.Where(d => d >= since).ToList();
            return Task.FromResult<IReadOnlyList<DateTimeOffset>>(filtered);
        }

        // The detector doesn't use these — minimal stubs.
        public Task SaveAsync(DetectedTask task, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DetectedTask?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<DetectedTask?>(null);
        public Task<IReadOnlyList<DetectedTask>> FindActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DetectedTask>>(Array.Empty<DetectedTask>());
        public Task<IReadOnlyList<DetectedTask>> FindRecentAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DetectedTask>>(Array.Empty<DetectedTask>());
        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> PurgeAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}

public sealed class LearningActionsTests
{
    [Fact]
    public async Task NeverRemindThisProgram_writes_anchored_ignore_rule()
    {
        var rules = new FakeRuleRepo();
        var settings = new FakeSettingsOverrides();
        var actions = new LearningActions(rules, settings, NullLogger<LearningActions>.Instance);

        var task = new DetectedTask(Guid.NewGuid(), "WMI", "rare.exe", 50, DateTimeOffset.UtcNow, 99, processName: "rare.exe");
        await actions.NeverRemindThisProgramAsync(task);

        var rule = Assert.Single(rules.Saved);
        Assert.Equal(RuleAction.Ignore, rule.Action);
        Assert.Equal(@"^rare\.exe$", rule.ProcessNamePattern);
        Assert.True(rule.IsUserCreated);
        Assert.Contains("rare.exe", rule.Name);
    }

    [Fact]
    public async Task AlwaysRemindForThisKind_writes_always_notify_rule()
    {
        var rules = new FakeRuleRepo();
        var settings = new FakeSettingsOverrides();
        var actions = new LearningActions(rules, settings, NullLogger<LearningActions>.Instance);

        var task = new DetectedTask(Guid.NewGuid(), "WMI", "build.exe", 50, DateTimeOffset.UtcNow, 99, processName: "build.exe");
        await actions.AlwaysRemindForThisKindAsync(task);

        var rule = Assert.Single(rules.Saved);
        Assert.Equal(RuleAction.AlwaysNotify, rule.Action);
    }

    [Fact]
    public async Task MarkAsBackgroundService_writes_resident_rule()
    {
        var rules = new FakeRuleRepo();
        var actions = new LearningActions(rules, new FakeSettingsOverrides(), NullLogger<LearningActions>.Instance);

        var task = new DetectedTask(Guid.NewGuid(), "WMI", "vite.exe", 50, DateTimeOffset.UtcNow, 99, processName: "vite.exe");
        await actions.MarkAsBackgroundServiceAsync(task);

        var rule = Assert.Single(rules.Saved);
        Assert.Equal(RuleAction.Resident, rule.Action);
    }

    [Fact]
    public async Task MarkAsDataTool_writes_broad_data_tool_pattern()
    {
        var rules = new FakeRuleRepo();
        var actions = new LearningActions(rules, new FakeSettingsOverrides(), NullLogger<LearningActions>.Instance);

        var task = new DetectedTask(Guid.NewGuid(), "WMI", "excel.exe", 50, DateTimeOffset.UtcNow, 99, processName: "excel.exe");
        await actions.MarkAsDataToolAsync(task);

        var rule = Assert.Single(rules.Saved);
        Assert.Equal(RuleAction.Ignore, rule.Action);
        Assert.Contains("excel", rule.ProcessNamePattern!);
        Assert.Contains("tableau", rule.ProcessNamePattern!);
    }

    [Fact]
    public async Task AdjustNotificationThreshold_persists_per_process_override()
    {
        var settings = new FakeSettingsOverrides();
        var actions = new LearningActions(new FakeRuleRepo(), settings, NullLogger<LearningActions>.Instance);

        var task = new DetectedTask(Guid.NewGuid(), "WMI", "build.exe", 50, DateTimeOffset.UtcNow, 99, processName: "build.exe");
        await actions.AdjustNotificationThresholdAsync(task, seconds: 5);

        Assert.Equal(5, settings.Overrides["build.exe"]);
    }

    [Fact]
    public async Task AdjustNotificationThreshold_zero_removes_override()
    {
        var settings = new FakeSettingsOverrides();
        settings.Overrides["build.exe"] = 99;
        var actions = new LearningActions(new FakeRuleRepo(), settings, NullLogger<LearningActions>.Instance);

        var task = new DetectedTask(Guid.NewGuid(), "WMI", "build.exe", 50, DateTimeOffset.UtcNow, 99, processName: "build.exe");
        await actions.AdjustNotificationThresholdAsync(task, seconds: 0);

        Assert.Empty(settings.Overrides);
    }

    [Fact]
    public async Task Repeat_call_on_same_program_updates_existing_rule_not_duplicate()
    {
        var rules = new FakeRuleRepo();
        var actions = new LearningActions(rules, new FakeSettingsOverrides(), NullLogger<LearningActions>.Instance);

        var task = new DetectedTask(Guid.NewGuid(), "WMI", "rare.exe", 50, DateTimeOffset.UtcNow, 99, processName: "rare.exe");
        await actions.NeverRemindThisProgramAsync(task);
        await actions.NeverRemindThisProgramAsync(task);

        Assert.Single(rules.Saved);
        Assert.Equal(2, rules.UpsertCallCount);
    }

    private sealed class FakeRuleRepo : IDetectionRuleRepository
    {
        public List<DetectionRule> Saved { get; } = new();
        public int UpsertCallCount;

        public Task<IReadOnlyList<DetectionRule>> LoadEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DetectionRule>>(Saved);
        public Task<IReadOnlyList<DetectionRule>> LoadUserRulesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DetectionRule>>(Saved);
        public Task SaveUserRuleAsync(DetectionRule rule, CancellationToken cancellationToken = default)
        { Saved.Add(rule); return Task.CompletedTask; }
        public Task UpsertUserRuleByNameAsync(DetectionRule rule, CancellationToken cancellationToken = default)
        {
            UpsertCallCount++;
            // Idempotent: replace by name.
            for (var i = 0; i < Saved.Count; i++)
            {
                if (Saved[i].Name == rule.Name) { Saved[i] = rule; return Task.CompletedTask; }
            }
            Saved.Add(rule);
            return Task.CompletedTask;
        }
        public Task<bool> DeleteUserRuleAsync(string name, CancellationToken cancellationToken = default)
        {
            var removed = Saved.RemoveAll(r => r.Name == name);
            return Task.FromResult(removed > 0);
        }
    }

    private sealed class FakeSettingsOverrides : IUserSettingsOverrides
    {
        public Dictionary<string, int> Overrides { get; } = new();
        public void MutateThreshold(string processName, int secondsOrZeroForDefault)
        {
            var key = processName.ToLowerInvariant();
            if (secondsOrZeroForDefault <= 0) Overrides.Remove(key);
            else Overrides[key] = secondsOrZeroForDefault;
        }
    }
}
