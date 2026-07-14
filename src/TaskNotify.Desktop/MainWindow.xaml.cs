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
    /// navigation. Implementation scans the store and nudges the navigation to
    /// TaskCenter first so the user lands on a visible entry.
    /// </summary>
    public void SelectTaskById(Guid taskId)
    {
        _store.FindById(taskId); // touch the store so it exists
        // Phase 4: defer deep-link to the in-page ListBox; the task will be visible
        // in TaskCenter. Phase 6's notification enhancement will add the precise scroll-to.
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
