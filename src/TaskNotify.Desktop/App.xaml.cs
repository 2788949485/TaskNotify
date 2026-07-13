using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace TaskNotify.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private MainWindow? _mainWindow;
    private TaskMonitorService? _monitor;
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection()
            .AddSingleton<TaskHistoryViewModel>()
            .AddSingleton<MainWindow>()
            .AddSingleton<TrayIconService>(_ => new(ShowMainWindow, ExitApplication))
            .AddSingleton<TaskMonitorService>();
        _services = services.BuildServiceProvider();
        _mainWindow = _services.GetRequiredService<MainWindow>();
        _tray = _services.GetRequiredService<TrayIconService>();
        _monitor = _services.GetRequiredService<TaskMonitorService>();
        _monitor.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Stop();
        _tray?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }

    private void ShowMainWindow()
    {
        _mainWindow?.Show();
        _mainWindow?.Activate();
    }

    private void ExitApplication()
    {
        if (_mainWindow is not null) _mainWindow.AllowClose = true;
        Shutdown();
    }
}
