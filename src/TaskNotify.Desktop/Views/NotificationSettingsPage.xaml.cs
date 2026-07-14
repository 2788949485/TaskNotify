using System.Windows;
using System.Windows.Controls;

namespace TaskNotify.Desktop.Views;

public partial class NotificationSettingsPage : System.Windows.Controls.UserControl
{
    public NotificationSettingsPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Forwards PasswordBox input to the ViewModel's <see cref="ViewModels.NotificationSettingsViewModel.EmailPassword"/>
    /// property without enabling TwoWay binding (WPF's binding stack would otherwise
    /// keep the plaintext password alive in shared state longer than necessary).
    /// </summary>
    private void EmailPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && DataContext is ViewModels.NotificationSettingsViewModel vm)
        {
            vm.EmailPassword = box.Password;
        }
    }
}
