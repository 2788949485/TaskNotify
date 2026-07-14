namespace TaskNotify.Core.Detection;

/// <summary>
/// Detection strategy (doc chapter 8). Higher modes track more process families
/// at the cost of more false positives.
/// </summary>
public enum DetectionMode
{
    /// <summary>
    /// Only processes that have signalled completion through a precise integration
    /// hook (Claude Code, Codex, Hermes, PowerShell, VS Code). No inference from
    /// process exit. Lowest false-positive rate. (doc 8.1)
    /// </summary>
    Precise,

    /// <summary>
    /// Default. Precise integrations plus WMI/snapshot detection on a curated
    /// shortlist (python, node, ffmpeg, build tools). Exit-without-exit-code
    /// becomes EndedUnknown. (doc 8.2)
    /// </summary>
    Balanced,

    /// <summary>
    /// Balanced plus speculative activity-based detection (CPU drop, file stability)
    /// and an expanded candidate set (java, dotnet, msbuild, devenv, cl, …).
    /// (doc 8.3)
    /// </summary>
    Broad
}
