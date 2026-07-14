using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public NotificationSettingsViewModel(AppSettingsStore settings)
    {
        _settings = settings;
        Title = "通知设置";

        var current = settings.Current;
        _cooldownSeconds = current.NotificationCooldownSeconds;
        _mergeBurstSeconds = current.MergeBurstSeconds;
        _notifyOnWaitingForPermission = current.NotifyOnWaitingForPermission;
        _notifyOnPossiblyCompleted = current.NotifyOnPossiblyCompleted;

        Thresholds = CollectionViewSource.GetDefaultView(ThresholdOverrides);
        RefreshOverrides(current);
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
