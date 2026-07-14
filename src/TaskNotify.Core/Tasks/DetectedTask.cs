using TaskNotify.Core.Detection;

namespace TaskNotify.Core.Tasks;

public sealed class DetectedTask
{
    public DetectedTask(
        Guid id,
        string source,
        string displayName,
        int taskProbability,
        DateTimeOffset detectedAt,
        int? rootProcessId = null,
        string? processName = null,
        string? workingDirectory = null,
        string? correlationKey = null)
    {
        Id = id;
        Source = source;
        DisplayName = displayName;
        TaskProbability = taskProbability;
        DetectedAt = detectedAt;
        RootProcessId = rootProcessId;
        ProcessName = processName;
        WorkingDirectory = workingDirectory;
        CorrelationKey = correlationKey;
    }

    public Guid Id { get; }
    public string Source { get; }
    public string DisplayName { get; private set; }
    public int TaskProbability { get; private set; }
    public CompletionConfidence CompletionConfidence { get; private set; }
    public TaskState State { get; private set; } = TaskState.Candidate;
    public int? RootProcessId { get; }
    public string? ProcessName { get; private set; }
    public string? CommandSummary { get; private set; }
    public string? WorkingDirectory { get; private set; }
    public DateTimeOffset DetectedAt { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public int? ExitCode { get; private set; }
    public string? ResultMessage { get; private set; }
    public string? OpenPath { get; private set; }
    public string? LogPath { get; private set; }
    public string? CorrelationKey { get; }
    public string MetadataJson { get; private set; } = "{}";

    public bool Apply(TaskSignal signal, CompletionConfidence confidence, DateTimeOffset occurredAt, int? exitCode = null)
    {
        if (!TaskStateMachine.TryTransition(State, signal, confidence, out var nextState))
        {
            return false;
        }

        if (nextState == State)
        {
            return true;
        }

        State = nextState;
        CompletionConfidence = Max(CompletionConfidence, confidence);
        StartedAt ??= signal == TaskSignal.Started ? occurredAt : null;
        if (TaskStateMachine.IsTerminal(nextState))
        {
            EndedAt = occurredAt;
            ExitCode = exitCode;
        }

        return true;
    }

    public void SetCommandSummary(string? commandSummary) => CommandSummary = CommandSanitizer.Sanitize(commandSummary);

    public void SetTaskProbability(int taskProbability) => TaskProbability = taskProbability;

    public void SetDisplayName(string displayName) => DisplayName = displayName;

    public void SetProcessName(string? processName) => ProcessName = processName;

    public void SetWorkingDirectory(string? workingDirectory) => WorkingDirectory = workingDirectory;

    public void SetResultMessage(string? resultMessage) => ResultMessage = resultMessage;

    public void SetOpenPath(string? openPath) => OpenPath = openPath;

    public void SetLogPath(string? logPath) => LogPath = logPath;

    public void SetMetadataJson(string metadataJson)
    {
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            MetadataJson = metadataJson;
        }
    }

    /// <summary>
    /// Produces a deep copy of the current task. Used by the persistence queue so
    /// that mutations after enqueue (e.g. subsequent Apply() calls) don't bleed
    /// into earlier queued snapshots.
    /// </summary>
    public DetectedTask Clone()
    {
        var copy = new DetectedTask(
            Id,
            Source,
            DisplayName,
            TaskProbability,
            DetectedAt,
            RootProcessId,
            ProcessName,
            WorkingDirectory,
            CorrelationKey)
        {
            CompletionConfidence = CompletionConfidence,
            State = State,
            CommandSummary = CommandSummary,
            StartedAt = StartedAt,
            EndedAt = EndedAt,
            ExitCode = ExitCode,
            ResultMessage = ResultMessage,
            OpenPath = OpenPath,
            LogPath = LogPath,
            MetadataJson = MetadataJson
        };
        return copy;
    }

    private static CompletionConfidence Max(CompletionConfidence left, CompletionConfidence right) =>
        left >= right ? left : right;
}
