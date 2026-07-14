using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNotify.Desktop.Notifications;
using TaskNotify.Infrastructure.Email;
using TaskNotify.Infrastructure.Settings;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// "通知设置" page (doc chapter 22). Toggles for speculative/permission-event
/// notifications, cooldown + burst-merge windows, and the per-process threshold
/// overrides. Real notification merging/cooldown behaviour lands in Phase 6; this
/// page only persists the preferences.
/// </summary>
public sealed partial class NotificationSettingsViewModel : PageViewModelBase
{
    private readonly AppSettingsStore _settings;
    private readonly EmailSettingsStore _emailSettings;
    private readonly EmailNotifier _emailNotifier;

    public NotificationSettingsViewModel(
        AppSettingsStore settings,
        EmailSettingsStore emailSettings,
        EmailNotifier emailNotifier)
    {
        _settings = settings;
        _emailSettings = emailSettings;
        _emailNotifier = emailNotifier;
        Title = "通知设置";

        var current = settings.Current;
        _cooldownSeconds = current.NotificationCooldownSeconds;
        _mergeBurstSeconds = current.MergeBurstSeconds;
        _notifyOnWaitingForPermission = current.NotifyOnWaitingForPermission;
        _notifyOnPossiblyCompleted = current.NotifyOnPossiblyCompleted;

        Thresholds = CollectionViewSource.GetDefaultView(ThresholdOverrides);
        RefreshOverrides(current);

        var email = emailSettings.Current;
        _emailEnabled = email.Enabled;
        _smtpHost = email.SmtpHost;
        _smtpPort = email.SmtpPort;
        _useSsl = email.UseSsl;
        _smtpUserName = email.SmtpUserName;
        _fromAddress = email.FromAddress;
        _fromDisplayName = email.FromDisplayName;
        _toAddresses = string.Join(", ", email.ToAddresses);
        _subjectPrefix = email.SubjectPrefix;
        _emailPassword = string.Empty;  // Never preload plaintext; user re-types if changing.
        _testSendStatus = "未发送";
    }

    public ObservableCollection<ThresholdOverride> ThresholdOverrides { get; } = new();
    public ICollectionView Thresholds { get; }

    [ObservableProperty]
    private int _cooldownSeconds;

    partial void OnCooldownSecondsChanged(int value)
        => _settings.Mutate(s => s.NotificationCooldownSeconds = value);

    [ObservableProperty]
    private int _mergeBurstSeconds;

    partial void OnMergeBurstSecondsChanged(int value)
        => _settings.Mutate(s => s.MergeBurstSeconds = value);

    [ObservableProperty]
    private bool _notifyOnWaitingForPermission;

    partial void OnNotifyOnWaitingForPermissionChanged(bool value)
        => _settings.Mutate(s => s.NotifyOnWaitingForPermission = value);

    [ObservableProperty]
    private bool _notifyOnPossiblyCompleted;

    partial void OnNotifyOnPossiblyCompletedChanged(bool value)
        => _settings.Mutate(s => s.NotifyOnPossiblyCompleted = value);

    [ObservableProperty]
    private string _newProcessName = string.Empty;

    [ObservableProperty]
    private int _newThresholdSeconds = 30;

    public void AddOverride()
    {
        var name = NewProcessName.Trim();
        if (name.Length == 0) return;
        _settings.Mutate(s => s.NotificationThresholdsSeconds[name.ToLowerInvariant()] = NewThresholdSeconds);
        NewProcessName = string.Empty;
        NewThresholdSeconds = 30;
        RefreshOverrides(_settings.Current);
    }

    [RelayCommand(CanExecute = nameof(CanAddOverride))]
    private void Add() => AddOverride();

    partial void OnNewProcessNameChanged(string value) => AddCommand.NotifyCanExecuteChanged();

    private bool CanAddOverride() => !string.IsNullOrWhiteSpace(NewProcessName);

    public void RemoveOverride(string key)
    {
        _settings.Mutate(s => s.NotificationThresholdsSeconds.Remove(key));
        RefreshOverrides(_settings.Current);
    }

    private void RefreshOverrides(AppSettings current)
    {
        ThresholdOverrides.Clear();
        foreach (var pair in current.NotificationThresholdsSeconds)
        {
            ThresholdOverrides.Add(new ThresholdOverride(pair.Key, pair.Value, RemoveOverride));
        }
    }

    // ----- Email section -----

    [ObservableProperty]
    private bool _emailEnabled;

    [ObservableProperty]
    private string _smtpHost = string.Empty;

    [ObservableProperty]
    private int _smtpPort = 587;

    [ObservableProperty]
    private bool _useSsl = true;

    [ObservableProperty]
    private string _smtpUserName = string.Empty;

    /// <summary>
    /// Plaintext password kept in memory only while the page is open. Bound to a
    /// PasswordBox via code-behind (not TwoWay binding) to avoid the WPF binding
    /// stack keeping it in the property store longer than necessary.
    /// </summary>
    [ObservableProperty]
    private string _emailPassword = string.Empty;

    [ObservableProperty]
    private string _fromAddress = string.Empty;

    [ObservableProperty]
    private string _fromDisplayName = "TaskNotify";

    [ObservableProperty]
    private string _toAddresses = string.Empty;

    [ObservableProperty]
    private string _subjectPrefix = "[TaskNotify]";

    [ObservableProperty]
    private string _testSendStatus = "未发送";

    [ObservableProperty]
    private bool _isSendingTest;

    partial void OnEmailEnabledChanged(bool value) => PersistEmailField(s => s.Enabled = value);
    partial void OnSmtpHostChanged(string value) => PersistEmailField(s => s.SmtpHost = value);
    partial void OnSmtpPortChanged(int value) => PersistEmailField(s => s.SmtpPort = value);
    partial void OnUseSslChanged(bool value) => PersistEmailField(s => s.UseSsl = value);
    partial void OnSmtpUserNameChanged(string value) => PersistEmailField(s => s.SmtpUserName = value);
    partial void OnFromAddressChanged(string value) => PersistEmailField(s => s.FromAddress = value);
    partial void OnFromDisplayNameChanged(string value) => PersistEmailField(s => s.FromDisplayName = value);
    partial void OnSubjectPrefixChanged(string value) => PersistEmailField(s => s.SubjectPrefix = value);

    private void PersistEmailField(Action<EmailSettings> mutator)
    {
        try { _emailSettings.Mutate(mutator); }
        catch (Exception ex)
        {
            TestSendStatus = "保存失败：" + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveEmail))]
    private void SaveEmail()
    {
        try
        {
            _emailSettings.Mutate(s =>
            {
                s.Enabled = EmailEnabled;
                s.SmtpHost = SmtpHost.Trim();
                s.SmtpPort = SmtpPort;
                s.UseSsl = UseSsl;
                s.SmtpUserName = SmtpUserName.Trim();
                s.FromAddress = FromAddress.Trim();
                s.FromDisplayName = string.IsNullOrWhiteSpace(FromDisplayName) ? "TaskNotify" : FromDisplayName.Trim();
                s.ToAddresses = ParseAddresses(ToAddresses);
                s.SubjectPrefix = string.IsNullOrWhiteSpace(SubjectPrefix) ? "[TaskNotify]" : SubjectPrefix.Trim();
            });

            // Only overwrite the password if the user typed one. Empty means "keep existing".
            if (!string.IsNullOrEmpty(EmailPassword))
            {
                _emailSettings.SetPassword(EmailPassword);
                EmailPassword = string.Empty;
            }
            TestSendStatus = "已保存";
        }
        catch (Exception ex)
        {
            TestSendStatus = "保存失败：" + ex.Message;
        }
    }

    private bool CanSaveEmail() => !IsSendingTest;

    [RelayCommand(CanExecute = nameof(CanSendTest))]
    private async Task TestSendEmailAsync()
    {
        SaveEmail();
        IsSendingTest = true;
        TestSendStatus = "发送中...";
        TestSendEmailCommand.NotifyCanExecuteChanged();
        SaveEmailCommand.NotifyCanExecuteChanged();
        try
        {
            var ok = await _emailNotifier.SendTestAsync();
            TestSendStatus = ok ? "发送成功" : "发送失败（查看 email.log）";
        }
        catch (Exception ex)
        {
            TestSendStatus = "发送失败：" + ex.Message;
        }
        finally
        {
            IsSendingTest = false;
            TestSendEmailCommand.NotifyCanExecuteChanged();
            SaveEmailCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSendTest() => !IsSendingTest;

    private static List<string> ParseAddresses(string raw)
    {
        var list = new List<string>();
        foreach (var part in raw.Split(',', ';', '\n', '\r'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0) list.Add(trimmed);
        }
        return list;
    }
}

/// <summary>One per-process threshold override row.</summary>
public sealed partial class ThresholdOverride : ObservableObject
{
    private readonly Action<string> _onRemove;

    public ThresholdOverride(string processName, int seconds, Action<string> onRemove)
    {
        _onRemove = onRemove;
        ProcessName = processName;
        Seconds = seconds;
    }

    public string ProcessName { get; }

    public int Seconds { get; }

    [RelayCommand]
    private void Remove() => _onRemove(ProcessName);
}
