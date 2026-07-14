using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// Page view model for menu entries that don't have a real page yet (Phase 5).
/// All 6 placeholders share this class; they differ only by <see cref="Title"/>.
/// </summary>
public sealed partial class PlaceholderViewModel : PageViewModelBase
{
    public PlaceholderViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    [ObservableProperty]
    private string _description = string.Empty;
}
