namespace TaskNotify.Infrastructure.Email;

/// <summary>
/// SMTP configuration stored at <c>%LOCALAPPDATA%\TaskNotify\email.json</c>.
/// Distinct from <see cref=" global::TaskNotify.Infrastructure.Settings.AppSettings"/>
/// so SMTP credentials never mix with general preferences.
/// </summary>
public sealed class EmailSettings
{
    public bool Enabled { get; set; }

    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>Default 587 (SMTP submission with STARTTLS).</summary>
    public int SmtpPort { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string SmtpUserName { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded DPAPI ciphertext (CurrentUser scope). Empty when no password
    /// is configured. Decryption only succeeds for the same Windows user that encrypted.
    /// </summary>
    public string SmtpPasswordEncrypted { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromDisplayName { get; set; } = "TaskNotify";

    /// <summary>Recipients. One mail is sent with all of them in the TO field.</summary>
    public List<string> ToAddresses { get; set; } = new();

    public string SubjectPrefix { get; set; } = "[TaskNotify]";
}
