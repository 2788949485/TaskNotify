namespace TaskNotify.Core.Performance;

/// <summary>
/// Hard caps that protect the host from runaway tracking load (doc chapter 29).
/// Above these limits new candidates are dropped silently; existing tasks keep running.
/// </summary>
public static class Limits
{
    /// <summary>Max simultaneously tracked processes in the tracker dictionary.</summary>
    public const int MaxTrackedProcesses = 500;

    /// <summary>Max simultaneously active task groups (root process + descendants).</summary>
    public const int MaxConcurrentTaskGroups = 100;

    /// <summary>Max bytes for a single IPC frame or command-line snapshot.</summary>
    public const int MaxEventBytes = 256 * 1024;

    /// <summary>DetectedTasks older than this are eligible for purge (doc chapter 9.3).</summary>
    public const int HistoryRetentionDays = 90;

    /// <summary>Log files older than this are deleted by FileLoggerProvider.</summary>
    public const int LogRetentionDays = 14;
}
