using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNotify.Integrations.Claude;
using TaskNotify.Integrations.Codex;
using TaskNotify.Integrations.Hermes;
using TaskNotify.Integrations.PowerShell;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// "集成管理" page (doc chapter 28.3). Shows the install state of each external
/// integration and lets the user install/reinstall from the UI without going
/// through the tray menu.
/// </summary>
public sealed partial class IntegrationManagerViewModel : PageViewModelBase
{
    public IntegrationManagerViewModel()
    {
        Title = "集成管理";
        Integrations =
        [
            new IntegrationEntry(
                "PowerShell",
                "PowerShell / WezTerm Profile",
                "在 PowerShell 和 WezTerm 中执行命令前后埋点，自动通知完成。",
                () => PowerShellProfileInstaller.IsInstalled(),
                () => PowerShellProfileInstaller.Install()),
            new IntegrationEntry(
                "Claude Code",
                "Claude Code Hook",
                "在 ~/.claude/settings.json 注册 Stop/Notification/PreToolUse Hook。",
                () => ClaudeSettingsManager.IsInstalled(),
                () => ClaudeSettingsManager.Install()),
            new IntegrationEntry(
                "Codex",
                "Codex Hook",
                "在 ~/.codex/hooks.json 注册 TaskNotify Hook。",
                () => CodexSettingsManager.IsInstalled(),
                () => CodexSettingsManager.Install()),
            new IntegrationEntry(
                "Hermes Agent",
                "Hermes Agent Hook",
                "在 Hermes 配置目录注册 TaskNotify Hook。",
                () => HermesSettingsManager.IsInstalled(),
                () => HermesSettingsManager.Install())
        ];
    }

    public ObservableCollection<IntegrationEntry> Integrations { get; }

    [RelayCommand]
    private void RefreshAll()
    {
        foreach (var entry in Integrations) entry.RefreshStatus();
    }
}

/// <summary>One integration card.</summary>
public sealed partial class IntegrationEntry : ObservableObject
{
    private readonly Func<bool> _isInstalled;
    private readonly Func<bool> _install;

    public IntegrationEntry(
        string key,
        string displayName,
        string description,
        Func<bool> isInstalled,
        Func<bool> install)
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
        _isInstalled = isInstalled;
        _install = install;
        _status = "检查中…";
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }

    [ObservableProperty]
    private string _status = "检查中…";

    [ObservableProperty]
    private bool _installed;

    [ObservableProperty]
    private string? _lastMessage;

    [RelayCommand]
    public void RefreshStatus()
    {
        try
        {
            Installed = _isInstalled();
            Status = Installed ? "已安装" : "未安装";
            LastMessage = null;
        }
        catch (Exception)
        {
            Installed = false;
            Status = "状态未知";
        }
    }

    [RelayCommand]
    public void InstallOrReinstall()
    {
        try
        {
            var changed = _install();
            RefreshStatus();
            LastMessage = changed ? "安装已更新，请重启对应终端或工具。" : "已是最新状态。";
        }
        catch (Exception)
        {
            Status = "安装失败";
            LastMessage = "安装失败；请检查对应配置文件权限或 Node.js 环境。";
        }
    }
}
