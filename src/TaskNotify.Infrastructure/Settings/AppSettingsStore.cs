using TaskNotify.Core.Interfaces;

namespace TaskNotify.Infrastructure.Settings;

/// <summary>
/// Thread-safe facade over <see cref="JsonSettingsStore"/> for
/// <see cref="AppSettings"/>. Reads return the latest in-memory snapshot
/// (refreshed on every successful write); writes are atomic and visible to
/// subsequent readers without a process restart.
/// </summary>
public sealed class AppSettingsStore : IUserSettingsOverrides
{
    private readonly JsonSettingsStore _store;
    private readonly object _gate = new();
    private AppSettings _current;

    public AppSettingsStore(JsonSettingsStore store)
    {
        _store = store;
        _current = store.Load<AppSettings>();
    }

    public string FilePath => _store.FilePath;

    /// <summary>
    /// Returns the current snapshot. Callers must not mutate the returned object;
    /// use <see cref="Mutate"/> for edits so the file and in-memory copy stay in sync.
    /// </summary>
    public AppSettings Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public void Mutate(Action<AppSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        lock (_gate)
        {
            _store.Mutate(mutator);
            _current = _store.Load<AppSettings>();
        }
    }

    /// <summary>Reloads from disk. Use after external edits (e.g. user hand-edited the file).</summary>
    public void Reload()
    {
        lock (_gate) _current = _store.Load<AppSettings>();
    }

    /// <inheritdoc />
    public void MutateThreshold(string processName, int secondsOrZeroForDefault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        Mutate(s =>
        {
            var key = processName.ToLowerInvariant();
            if (secondsOrZeroForDefault <= 0) s.NotificationThresholdsSeconds.Remove(key);
            else s.NotificationThresholdsSeconds[key] = secondsOrZeroForDefault;
        });
    }
}
