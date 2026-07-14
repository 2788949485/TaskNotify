using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskNotify.Desktop.ViewModels;

/// <summary>
/// Common base for page view models. Carries the page title that the shell can
/// show in the header strip.
/// </summary>
public abstract partial class PageViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;
}
