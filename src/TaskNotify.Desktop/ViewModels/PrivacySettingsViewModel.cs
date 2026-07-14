using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNotify.Core.Interfaces;
using TaskNotify.Infrastructure.Logging;
using TaskNotify.Infrastructure.Settings;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// "隐私设置" page (doc chapter 26). Manages extra command-line sanitizer patterns,
/// local-data folder display, history retention toggles, and the "clear history now" button.
/// </summary>
public sealed partial class PrivacySettingsViewModel : PageViewModelBase
{
    private readonly AppSettingsStore _settings;
    private readonly IDetectedTaskRepository _taskRepo;
    private readonly JsonSettingsStore _jsonStore;

    public PrivacySettingsViewModel(
        AppSettingsStore settings,
        IDetectedTaskRepository taskRepo,
        JsonSettingsStore jsonStore)
    {
        _settings = settings;
        _taskRepo = taskRepo;
        _jsonStore = jsonStore;
        Title = "隐私设置";

        var current = settings.Current;
        _clearHistoryOnExit = current.Privacy.ClearHistoryOnExit;
        _disableHistory = current.Privacy.DisableHistory;

        foreach (var pattern in current.Privacy.ExtraSensitivePatterns)
        {
            Patterns.Add(pattern);
        }

        var root = Path.GetDirectoryName(_jsonStore.FilePath) ?? string.Empty;
        SettingsFilePath = _jsonStore.FilePath;
        DatabasePath = Path.Combine(root, "tasknotify.db");
        LogDirectory = FileLoggerProvider.DefaultLogDirectory();
    }

    public ObservableCollection<string> Patterns { get; } = new();

    public string SettingsFilePath { get; }
    public string DatabasePath { get; }
    public string LogDirectory { get; }

    [ObservableProperty]
    private bool _clearHistoryOnExit;

    partial void OnClearHistoryOnExitChanged(bool value)
        => _settings.Mutate(s => s.Privacy.ClearHistoryOnExit = value);

    [ObservableProperty]
    private bool _disableHistory;

    partial void OnDisableHistoryChanged(bool value)
        => _settings.Mutate(s => s.Privacy.DisableHistory = value);

    [ObservableProperty]
    private string _newPattern = string.Empty;

    [ObservableProperty]
    private string? _lastMessage;

    [RelayCommand(CanExecute = nameof(CanAddPattern))]
    private void AddPattern()
    {
        var value = NewPattern.Trim();
        if (value.Length == 0) return;
        Patterns.Add(value);
        PersistPatterns();
        NewPattern = string.Empty;
    }

    partial void OnNewPatternChanged(string value) => AddPatternCommand.NotifyCanExecuteChanged();

    private bool CanAddPattern() => !string.IsNullOrWhiteSpace(NewPattern);

    [RelayCommand]
    private void RemovePattern(string? pattern)
    {
        if (pattern is not null && Patterns.Remove(pattern))
        {
            PersistPatterns();
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        try
        {
            var count = await _taskRepo.PurgeAllAsync().ConfigureAwait(true);
            LastMessage = $"已清除 {count} 条历史记录。";
        }
        catch (Exception)
        {
            LastMessage = "清除失败；请查看日志。";
        }
    }

    private void PersistPatterns()
    {
        var snapshot = Patterns.ToList();
        _settings.Mutate(s => s.Privacy.ExtraSensitivePatterns = snapshot);
    }
}
