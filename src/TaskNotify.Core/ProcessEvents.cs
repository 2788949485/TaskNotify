namespace TaskNotify.Core;

public sealed record ProcessIdentity(int ProcessId, DateTimeOffset StartedAt);

public abstract record ProcessLifecycleEvent(DateTimeOffset OccurredAt);

public sealed record ProcessStartedEvent(
    ProcessIdentity Identity,
    int? ParentProcessId,
    string ProcessName,
    DateTimeOffset OccurredAt,
    string? ExecutablePath = null,
    string? CommandLine = null,
    string? ParentProcessName = null) : ProcessLifecycleEvent(OccurredAt);

public sealed record ProcessStoppedEvent(
    int ProcessId,
    string ProcessName,
    DateTimeOffset OccurredAt,
    ProcessIdentity? Identity = null) : ProcessLifecycleEvent(OccurredAt);
