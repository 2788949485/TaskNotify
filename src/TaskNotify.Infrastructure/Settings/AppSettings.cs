using TaskNotify.Core.Detection;

namespace TaskNotify.Infrastructure.Settings;

/// <summary>
/// Application-side user preferences. Stored as JSON at
/// <c>%LOCALAPPDATA%\TaskNotify\settings.json</c>. Distinct from per-integration
/// settings (Claude settings.json, Codex hooks.json, etc.) which live elsewhere.
/// </summary>
public sealed class AppSettings
{
    public DetectionMode DetectionMode { get; set; } = DetectionMode.Balanced;

    /// <summary>
    /// Per-source kill switches. Keys are integration/source identifiers
    /// ("wmi", "claude", "codex", "hermes", "powershell", "vscode").
    /// Missing keys are treated as enabled.
    /// </summary>
    public Dictionary<string, bool> EnabledSources { get; set; } = new()
    {
        ["wmi"] = true,
        ["claude"] = true,
        ["codex"] = true,
        ["hermes"] = true,
        ["powershell"] = true,
        ["vscode"] = false
    };

    /// <summary>
    /// Per-process minimum-duration overrides (seconds) before a notification fires.
    /// Missing keys fall back to <see cref="Detection.NotificationThreshold"/>.
    /// </summary>
    public Dictionary<string, int> NotificationThresholdsSeconds { get; set; } = new();

    /// <summary>
    /// Cooldown (seconds) between notifications for the same task (doc 22.4).
    /// WaitingForPermission events bypass cooldown. Default 10s.
    /// </summary>
    public int NotificationCooldownSeconds { get; set; } = 10;

    /// <summary>
    /// Window (seconds) in which completion events for the same task merge into one toast (doc 22.3).
    /// Default 5s.
    /// </summary>
    public int MergeBurstSeconds { get; set; } = 5;

    /// <summary>If true, fire a toast when a task enters WaitingForPermission regardless of cooldown.</summary>
    public bool NotifyOnWaitingForPermission { get; set; } = true;

    /// <summary>If true, fire a toast when a task enters PossiblyCompleted (speculative). Default off — can be noisy.</summary>
    public bool NotifyOnPossiblyCompleted { get; set; } = false;

    public PrivacySettings Privacy { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
}

public sealed class PrivacySettings
{
    /// <summary>
    /// Additional keyword patterns stripped from command lines on top of the
    /// built-in <c>CommandSanitizer</c> defaults. Each entry is a regex.
    /// </summary>
    public List<string> ExtraSensitivePatterns { get; set; } = new();

    /// <summary>If true, DetectedTasks are wiped on shutdown (no history).</summary>
    public bool ClearHistoryOnExit { get; set; } = false;

    /// <summary>If true, detected tasks never persist to disk.</summary>
    public bool DisableHistory { get; set; } = false;
}

public sealed class PerformanceSettings
{
    /// <summary>Overrides <c>Limits.MaxTrackedProcesses</c> when positive.</summary>
    public int MaxTrackedProcesses { get; set; } = 0;

    /// <summary>Overrides <c>Limits.MaxConcurrentTaskGroups</c> when positive.</summary>
    public int MaxConcurrentTaskGroups { get; set; } = 0;

    /// <summary>Overrides <c>Limits.HistoryRetentionDays</c> when positive.</summary>
    public int HistoryRetentionDays { get; set; } = 0;
}
