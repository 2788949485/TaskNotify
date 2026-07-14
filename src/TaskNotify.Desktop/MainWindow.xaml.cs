using System.Windows;
using TaskNotify.Desktop.ViewModels;

namespace TaskNotify.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TaskHistoryViewModel _store;

    public MainWindow(MainViewModel viewModel, TaskHistoryViewModel store)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _store = store;
        DataContext = viewModel;
    }

    /// <summary>
    /// Selects the task matching <paramref name="taskId"/> in the task list, if
    /// the current page is one of the task-filter views. Used by toast-click
    /// navigation. Hands off to <see cref="MainViewModel.SelectTaskInCenter"/>.
    /// </summary>
    public void SelectTaskById(Guid taskId)
    {
        _viewModel.SelectTaskInCenter(taskId);
    }

    public bool AllowClose { get; set; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
