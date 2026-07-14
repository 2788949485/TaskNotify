using Microsoft.Win32;

namespace TaskNotify.Desktop.Startup;

/// <summary>
/// Manages the HKCU "Run" entry that starts TaskNotify on user login.
/// HKCU scope — no admin elevation required. (doc chapter 27.2)
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "TaskNotify";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void Install()
    {
        var path = CurrentExecutablePath();
        if (path is null) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(AppName, $"\"{path}\"");
    }

    public static void Uninstall()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch
        {
            // Swallow: best-effort. The value may already be gone, or the user may lack permissions.
        }
    }

    private static string? CurrentExecutablePath() =>
        Environment.ProcessPath
        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
}
