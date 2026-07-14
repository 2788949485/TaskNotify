using System.Text.RegularExpressions;

namespace TaskNotify.Core.Detection;

public enum RuleAction
{
    AdjustScore,
    Ignore,
    AlwaysNotify,
    Resident
}

public enum TaskProbability
{
    Ignored,
    Candidate,
    High,
    Exact
}

public sealed record DetectionRule(
    string Name,
    int ScoreAdjustment = 0,
    RuleAction Action = RuleAction.AdjustScore,
    string? ProcessNamePattern = null,
    string? ExecutablePathPattern = null,
    string? CommandLinePattern = null,
    string? ParentProcessNamePattern = null,
    string? AncestorProcessNamePattern = null,
    TimeSpan? MinimumDuration = null,
    bool IsUserCreated = false);

public sealed record ProcessCandidate(
    int ProcessId,
    DateTimeOffset StartedAt,
    string ProcessName,
    string? ExecutablePath = null,
    string? CommandLine = null,
    string? ParentProcessName = null,
    IReadOnlyCollection<string>? AncestorProcessNames = null);

public sealed record DetectionResult(TaskProbability Probability, int Score, bool IsResident, bool MeetsMinimumDuration, IReadOnlyList<string> MatchedRules)
{
    public bool ShouldNotify => (Probability is TaskProbability.High or TaskProbability.Exact) && !IsResident && MeetsMinimumDuration;
}

public sealed class DetectionRuleEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public DetectionResult Evaluate(
        ProcessCandidate candidate,
        TimeSpan runningFor,
        IEnumerable<DetectionRule> builtInRules,
        IEnumerable<DetectionRule>? userRules = null,
        IReadOnlyDictionary<string, int>? thresholdOverrides = null)
    {
        var matched = new List<string>();
        var score = 0;
        var isResident = false;
        var alwaysNotify = false;

        foreach (var rule in (userRules ?? []).Where(rule => Matches(rule, candidate, runningFor)))
        {
            matched.Add(rule.Name);
            if (rule.Action == RuleAction.Ignore)
            {
                return new(TaskProbability.Ignored, 0, false, false, matched);
            }

            if (rule.Action == RuleAction.Resident)
            {
                return new(TaskProbability.Ignored, 0, true, false, matched);
            }

            if (rule.Action == RuleAction.AlwaysNotify)
            {
                alwaysNotify = true;
                score = Math.Max(score, 100);
            }
            else
            {
                score += rule.ScoreAdjustment;
            }
        }

        foreach (var rule in builtInRules.Where(rule => Matches(rule, candidate, runningFor)))
        {
            matched.Add(rule.Name);
            if (rule.Action == RuleAction.Ignore && !alwaysNotify)
            {
                return new(TaskProbability.Ignored, 0, isResident, false, matched);
            }

            isResident |= rule.Action == RuleAction.Resident;
            score += rule.ScoreAdjustment;
        }

        return new(ToProbability(score), score, isResident, runningFor >= NotificationThreshold.For(candidate.ProcessName, thresholdOverrides), matched);
    }

    public static bool IsValid(DetectionRule rule) => Patterns(rule).All(IsValidPattern);

    private static bool Matches(DetectionRule rule, ProcessCandidate candidate, TimeSpan runningFor) =>
        (!rule.MinimumDuration.HasValue || runningFor >= rule.MinimumDuration) &&
        Matches(rule.ProcessNamePattern, candidate.ProcessName) &&
        Matches(rule.ExecutablePathPattern, candidate.ExecutablePath) &&
        Matches(rule.CommandLinePattern, candidate.CommandLine) &&
        Matches(rule.ParentProcessNamePattern, candidate.ParentProcessName) &&
        (rule.AncestorProcessNamePattern is null || candidate.AncestorProcessNames?.Any(name => Matches(rule.AncestorProcessNamePattern, name)) == true);

    private static IEnumerable<string?> Patterns(DetectionRule rule)
    {
        yield return rule.ProcessNamePattern;
        yield return rule.ExecutablePathPattern;
        yield return rule.CommandLinePattern;
        yield return rule.ParentProcessNamePattern;
        yield return rule.AncestorProcessNamePattern;
    }

    private static bool IsValidPattern(string? pattern)
    {
        if (pattern is null) return true;
        try { _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout); return true; }
        catch (ArgumentException) { return false; }
    }

    private static bool Matches(string? pattern, string? value)
    {
        if (pattern is null) return true;
        if (value is null) return false;
        try { return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static TaskProbability ToProbability(int score) => score switch
    {
        >= 80 => TaskProbability.Exact,
        >= 50 => TaskProbability.High,
        >= 30 => TaskProbability.Candidate,
        _ => TaskProbability.Ignored
    };
}

public static class NotificationThreshold
{
    public static TimeSpan For(string processName) => For(processName, null);

    /// <summary>
    /// Resolves the minimum-running duration before a notification fires for this
    /// process. If <paramref name="overrides"/> contains a positive entry for the
    /// process (lower-cased, with or without extension), it takes precedence over
    /// the built-in defaults.
    /// </summary>
    public static TimeSpan For(string processName, IReadOnlyDictionary<string, int>? overrides)
    {
        if (overrides is not null && overrides.Count > 0)
        {
            var key = processName.ToLowerInvariant();
            if (overrides.TryGetValue(key, out var seconds) && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return processName.ToLowerInvariant() switch
        {
            "ffmpeg.exe" => TimeSpan.FromSeconds(10),
            "python.exe" or "pythonw.exe" or "py.exe" or "node.exe" or "npm.exe" or "npm.cmd" or "pnpm.exe" or "pnpm.cmd" or "yarn.exe" or "yarn.cmd" => TimeSpan.FromSeconds(20),
            _ => TimeSpan.FromSeconds(60)
        };
    }
}

public static class BuiltInDetectionRules
{
    /// <summary>
    /// Balanced (default, doc 8.2). A curated shortlist of build/toolchain
    /// processes plus a couple of suppress-filters for resident noise.
    /// </summary>
    public static IReadOnlyList<DetectionRule> Balanced { get; } =
    [
        new("系统或后台关键词", Action: RuleAction.Ignore, CommandLinePattern: @"\b(language-server|language_server|pylance|debugpy|extensionHost|tsserver|electron|telemetry|update)\b"),
        new("常驻开发服务器", Action: RuleAction.Ignore, CommandLinePattern: @"\b(watch|dev-server|vite\s+--host|webpack\s+serve)\b"),
        new("Python", 30, ProcessNamePattern: @"^(python|pythonw|py)\.exe$"),
        new("Node", 30, ProcessNamePattern: @"^(node|npm|pnpm|yarn)\.(exe|cmd)$"),
        new("ffmpeg", 30, ProcessNamePattern: @"^ffmpeg\.exe$"),
        new("终端父进程", 20, ParentProcessNamePattern: @"^(WindowsTerminal|wezterm-gui|alacritty|powershell|pwsh|cmd|Gateway)\.exe$"),
        new("IDE 父进程", 15, ParentProcessNamePattern: @"^(Code|devenv|pycharm64|idea64)\.exe$"),
        new("任务命令", 20, CommandLinePattern: @"\b(build|test|train|process)\b"),
        new("运行超过 30 秒", 15, MinimumDuration: TimeSpan.FromSeconds(30))
    ];

    /// <summary>
    /// Precise (doc 8.1). No inference from raw process exit; only integration
    /// hooks notify. Achieved by returning an empty rule set so WMI candidates
    /// score 0 → Ignored → never tracked, never notified.
    /// </summary>
    public static IReadOnlyList<DetectionRule> Precise { get; } = Array.Empty<DetectionRule>();

    /// <summary>
    /// Broad (doc 8.3). Balanced plus an expanded toolchain shortlist
    /// (java, dotnet, msbuild, cl, cargo, rustc, cmake, ninja, …).
    /// </summary>
    public static IReadOnlyList<DetectionRule> Broad { get; } =
    [
        new("系统或后台关键词", Action: RuleAction.Ignore, CommandLinePattern: @"\b(language-server|language_server|pylance|debugpy|extensionHost|tsserver|electron|telemetry|update)\b"),
        new("常驻开发服务器", Action: RuleAction.Ignore, CommandLinePattern: @"\b(watch|dev-server|vite\s+--host|webpack\s+serve)\b"),
        new("Python", 30, ProcessNamePattern: @"^(python|pythonw|py)\.exe$"),
        new("Node", 30, ProcessNamePattern: @"^(node|npm|pnpm|yarn)\.(exe|cmd)$"),
        new("ffmpeg", 30, ProcessNamePattern: @"^ffmpeg\.exe$"),
        new("Java", 30, ProcessNamePattern: @"^java(w)?\.exe$"),
        new(".NET", 30, ProcessNamePattern: @"^(dotnet|msbuild|vstest\.console)\.exe$"),
        new("Native build", 30, ProcessNamePattern: @"^(cl|link|cargo|rustc|cmake|ninja|make|g\+\+|gcc)\.exe$"),
        new("终端父进程", 20, ParentProcessNamePattern: @"^(WindowsTerminal|wezterm-gui|alacritty|powershell|pwsh|cmd|Gateway)\.exe$"),
        new("IDE 父进程", 15, ParentProcessNamePattern: @"^(Code|devenv|pycharm64|idea64)\.exe$"),
        new("任务命令", 20, CommandLinePattern: @"\b(build|test|train|process|compile|link|deploy|pack|publish)\b"),
        new("运行超过 30 秒", 15, MinimumDuration: TimeSpan.FromSeconds(30))
    ];

    /// <summary>Resolves the rule set for the given mode.</summary>
    public static IReadOnlyList<DetectionRule> For(DetectionMode mode) => mode switch
    {
        DetectionMode.Precise => Precise,
        DetectionMode.Broad => Broad,
        _ => Balanced
    };
}
