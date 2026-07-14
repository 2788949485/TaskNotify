using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNotify.Desktop.Startup;
using TaskNotify.Infrastructure.Logging;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// "系统设置" page (doc chapter 27). Auto-start toggle, log path display, version,
/// and the uninstall entry point (Phase 9 ships the real installer).
/// </summary>
public sealed partial class SystemSettingsViewModel : PageViewModelBase
{
    public SystemSettingsViewModel()
    {
        Title = "系统设置";
        _autoStartEnabled = AutoStartManager.IsEnabled();
        LogDirectory = FileLoggerProvider.DefaultLogDirectory();
        Version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                  ?? "未知";
    }

    public string LogDirectory { get; }

    public string Version { get; }

    [ObservableProperty]
    private bool _autoStartEnabled;

    partial void OnAutoStartEnabledChanged(bool value)
    {
        try
        {
            if (value) AutoStartManager.Install();
            else AutoStartManager.Uninstall();
            LastMessage = value ? "已设置开机启动。" : "已取消开机启动。";
        }
        catch (Exception)
        {
            LastMessage = "更改开机启动失败；请检查注册表权限。";
            AutoStartEnabled = AutoStartManager.IsEnabled();
        }
    }

    [ObservableProperty]
    private string? _lastMessage;

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            if (!System.IO.Directory.Exists(LogDirectory)) System.IO.Directory.CreateDirectory(LogDirectory);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{LogDirectory}\"");
        }
        catch
        {
            LastMessage = "无法打开日志目录。";
        }
    }
}
