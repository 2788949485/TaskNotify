using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskNotify.Core.Detection;
using TaskNotify.Infrastructure.Settings;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// "自动检测" page (doc chapter 24.2). Three modes (Precise/Balanced/Broad)
/// and per-source kill switches. Edits persist immediately via <see cref="AppSettingsStore.Mutate"/>.
/// </summary>
public sealed partial class DetectionSettingsViewModel : PageViewModelBase
{
    private readonly AppSettingsStore _settings;

    public DetectionSettingsViewModel(AppSettingsStore settings)
    {
        _settings = settings;
        Title = "自动检测";

        var current = settings.Current;
        _selectedMode = current.DetectionMode;

        Sources =
        [
            new("wmi", "系统进程监控 (WMI/快照)", IsEnabled(current, "wmi"), ApplySource),
            new("claude", "Claude Code Hook", IsEnabled(current, "claude"), ApplySource),
            new("codex", "Codex Hook", IsEnabled(current, "codex"), ApplySource),
            new("hermes", "Hermes Agent Hook", IsEnabled(current, "hermes"), ApplySource),
            new("powershell", "PowerShell Profile", IsEnabled(current, "powershell"), ApplySource),
            new("vscode", "VS Code 扩展", IsEnabled(current, "vscode"), ApplySource)
        ];
    }

    public ObservableCollection<SourceToggle> Sources { get; }

    [ObservableProperty]
    private DetectionMode _selectedMode;

    partial void OnSelectedModeChanged(DetectionMode value)
        => _settings.Mutate(s => s.DetectionMode = value);

    private void ApplySource(string key, bool enabled)
        => _settings.Mutate(s => s.EnabledSources[key] = enabled);

    private static bool IsEnabled(AppSettings current, string key)
        => current.EnabledSources.TryGetValue(key, out var enabled) ? enabled : true;
}

/// <summary>One row in the detection-source checklist.</summary>
public sealed partial class SourceToggle : ObservableObject
{
    private readonly Action<string, bool> _onChange;

    public SourceToggle(string key, string displayName, bool enabled, Action<string, bool> onChange)
    {
        Key = key;
        DisplayName = displayName;
        _enabled = enabled;
        _onChange = onChange;
    }

    public string Key { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _enabled;

    partial void OnEnabledChanged(bool value) => _onChange(Key, value);
}
