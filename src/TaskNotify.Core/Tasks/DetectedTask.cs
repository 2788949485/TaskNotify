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
        int? rootProcessId = null)
    {
        Id = id;
        Source = source;
        DisplayName = displayName;
        TaskProbability = taskProbability;
        DetectedAt = detectedAt;
        RootProcessId = rootProcessId;
    }

    public Guid Id { get; }
    public string Source { get; }
    public string DisplayName { get; private set; }
    public int TaskProbability { get; private set; }
    public CompletionConfidence CompletionConfidence { get; private set; }
    public TaskState State { get; private set; } = TaskState.Candidate;
    public int? RootProcessId { get; }
    public DateTimeOffset DetectedAt { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public int? ExitCode { get; private set; }
    public string? CommandSummary { get; private set; }

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

    private static CompletionConfidence Max(CompletionConfidence left, CompletionConfidence right) =>
        left >= right ? left : right;
}
