using System.Windows;

namespace TaskNotify.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(TaskHistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
