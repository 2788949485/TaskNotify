using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskNotify.Desktop.Models;

namespace TaskNotify.Desktop.Services;

/// <summary>
/// Static catalogue of the 9 left-menu entries (doc chapter 24). Phase 4 ships
/// the first 3 as real pages; the rest are placeholders that Phase 5 fills in.
///
/// For real pages <see cref="AppMenuItem.ViewModelType"/> points at the DI
/// singleton the navigator resolves. For placeholders it's null — the navigator
/// just constructs a <c>PlaceholderViewModel</c> with the title.
/// </summary>
public static class MenuCatalog
{
    public static IReadOnlyList<AppMenuItem> All { get; } =
    [
        new("任务中心", "", typeof(ViewModels.TaskCenterViewModel)),
        new("正在运行", "", typeof(ViewModels.RunningTasksViewModel)),
        new("最近完成", "", typeof(ViewModels.RecentCompletedViewModel)),
        new("自动检测", "", typeof(ViewModels.DetectionSettingsViewModel)),
        new("程序规则", "", typeof(ViewModels.ProgramRulesViewModel)),
        new("集成管理", "", typeof(ViewModels.IntegrationManagerViewModel)),
        new("通知设置", "", typeof(ViewModels.NotificationSettingsViewModel)),
        new("隐私设置", "", typeof(ViewModels.PrivacySettingsViewModel)),
        new("系统设置", "", typeof(ViewModels.SystemSettingsViewModel))
    ];
}
