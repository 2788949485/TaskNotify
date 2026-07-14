namespace TaskNotify.Core.Interfaces;

/// <summary>
/// Read/write contract for the user-configurable parts of <c>AppSettings</c>.
/// Defined in Core so the learning service can mutate thresholds without
/// depending on Infrastructure. AppSettingsStore is the canonical implementer.
/// </summary>
public interface IUserSettingsOverrides
{
    /// <summary>
    /// Sets or clears a per-process minimum-duration override.
    /// Pass <paramref name="secondsOrZeroForDefault"/> = 0 to reset to default.
    /// </summary>
    void MutateThreshold(string processName, int secondsOrZeroForDefault);
}
