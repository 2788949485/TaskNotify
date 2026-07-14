using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNotify.Core.Detection;
using TaskNotify.Core.Interfaces;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// "程序规则" page (doc chapter 24.3). Lists user-created detection rules
/// (Ignore / AlwaysNotify / Resident) and lets the user add or delete them.
/// Built-in rules are not editable.
/// </summary>
public sealed partial class ProgramRulesViewModel : PageViewModelBase
{
    private readonly IDetectionRuleRepository _repo;

    public ProgramRulesViewModel(IDetectionRuleRepository repo)
    {
        _repo = repo;
        Title = "程序规则";
        AvailableActions =
        [
            RuleAction.Ignore,
            RuleAction.AlwaysNotify,
            RuleAction.Resident
        ];
        _ = LoadAsync();
    }

    public ObservableCollection<DetectionRule> Rules { get; } = new();

    public IReadOnlyList<RuleAction> AvailableActions { get; }

    [ObservableProperty]
    private string _newProcessName = string.Empty;

    [ObservableProperty]
    private RuleAction _newAction = RuleAction.Ignore;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var rules = await _repo.LoadUserRulesAsync().ConfigureAwait(true);
        Rules.Clear();
        foreach (var rule in rules) Rules.Add(rule);
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        var name = NewProcessName.Trim();
        if (name.Length == 0) return;

        var rule = new DetectionRule(
            Name: RuleNameFor(name, NewAction),
            Action: NewAction,
            ProcessNamePattern: AnchorPattern(name),
            IsUserCreated: true);

        await _repo.UpsertUserRuleByNameAsync(rule).ConfigureAwait(true);
        NewProcessName = string.Empty;
        await LoadAsync().ConfigureAwait(true);
    }

    partial void OnNewProcessNameChanged(string value) => AddCommand.NotifyCanExecuteChanged();

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewProcessName);

    [RelayCommand]
    private async Task DeleteAsync(DetectionRule? rule)
    {
        if (rule is null) return;
        await _repo.DeleteUserRuleAsync(rule.Name).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    private static string RuleNameFor(string processName, RuleAction action)
        => $"user:{processName.ToLowerInvariant()}:{action}";

    private static string AnchorPattern(string processName) =>
        "^" + Regex.Escape(processName) + "$";
}
