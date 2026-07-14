using System.Windows;
using WpfListBox = System.Windows.Controls.ListBox;

namespace TaskNotify.Desktop;

public partial class MainWindow : Window
{
    private readonly TaskHistoryViewModel _viewModel;
    private WpfListBox? _listBox;

    public MainWindow(TaskHistoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _listBox = FindVisualChild<WpfListBox>(this);
    }

    /// <summary>
    /// 根据 TaskId 选中对应列表项并滚动到可视区域。
    /// </summary>
    public void SelectTaskById(Guid taskId)
    {
        var vm = _viewModel.FindById(taskId);
        if (vm is null || _listBox is null) return;

        _listBox.SelectedItem = vm;

        // 确保列表项在可视区域内
        _listBox.ScrollIntoView(vm);
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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }
}
