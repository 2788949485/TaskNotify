using System.Windows.Controls;

namespace TaskNotify.Desktop.Views;

/// <summary>
/// Code-behind for TaskListPage.xaml. Shared by all three task-filter pages
/// (TaskCenter / RunningTasks / RecentCompleted) because they have identical
/// visuals and differ only in their filter predicate.
/// </summary>
public partial class TaskListPage : System.Windows.Controls.UserControl
{
    public TaskListPage()
    {
        InitializeComponent();
    }
}
