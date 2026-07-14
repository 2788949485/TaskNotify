using TaskNotify.Infrastructure.Settings;

namespace TaskNotify.Infrastructure.Email;

/// <summary>
/// Thread-safe facade over <see cref="JsonSettingsStore"/> for
/// <see cref="EmailSettings"/>. Reads return the latest in-memory snapshot
/// (refreshed on every successful write); writes are atomic.
///
/// Default location: <c>%LOCALAPPDATA%\TaskNotify\email.json</c> — alongside
/// <c>settings.json</c> but in a separate file so SMTP credentials stay isolated.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class EmailSettingsStore
{
    private readonly JsonSettingsStore _store;
    private readonly object _gate = new();
    private EmailSettings _current;

    public EmailSettingsStore()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(root, "TaskNotify", "email.json");
        _store = new JsonSettingsStore(path);
        _current = _store.Load<EmailSettings>();
    }

    /// <summary>For tests / custom locations.</summary>
    public EmailSettingsStore(string customPath)
    {
        _store = new JsonSettingsStore(customPath);
        _current = _store.Load<EmailSettings>();
    }

    public string FilePath => _store.FilePath;

    /// <summary>
    /// Returns the current snapshot. Callers must not mutate the returned object;
    /// use <see cref="Mutate"/> so the file stays in sync.
    /// </summary>
    public EmailSettings Current
    {
        get { lock (_gate) return _current; }
    }

    public void Mutate(Action<EmailSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        lock (_gate)
        {
            _store.Mutate(mutator);
            _current = _store.Load<EmailSettings>();
        }
    }

    /// <summary>Decrypts the stored SMTP password. Empty if unset or undecryptable.</summary>
    public string GetDecryptedPassword()
    {
        var encrypted = Current.SmtpPasswordEncrypted;
        return EmailPasswordProtector.Decrypt(encrypted);
    }

    /// <summary>Encrypts and stores the given plaintext password.</summary>
    public void SetPassword(string plain)
    {
        Mutate(s => s.SmtpPasswordEncrypted = EmailPasswordProtector.Encrypt(plain));
    }
}
