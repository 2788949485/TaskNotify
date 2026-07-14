using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskNotify.Desktop.Services;

/// <summary>
/// Holds the currently visible page ViewModel. The MainWindow ContentControl binds
/// to <see cref="CurrentPage"/>; DataTemplates in App.xaml map each VM type to its
/// XAML view. Lives as a singleton in DI so tray-icon handlers and notification
/// callbacks can drive navigation (e.g. jumping to a task when its toast is clicked).
/// </summary>
public sealed partial class NavigationService : ObservableObject
{
    [ObservableProperty]
    private object? _currentPage;

    /// <summary>
    /// Marshals navigation to the dispatcher — required because callers
    /// (notification handlers, IPC) may not be on the UI thread.
    /// </summary>
    public void Navigate(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is { } dispatcher)
        {
            dispatcher.BeginInvoke(new Action(() => CurrentPage = viewModel));
        }
        else
        {
            CurrentPage = viewModel;
        }
    }
}
