using System.Text.RegularExpressions;

namespace TaskNotify.Integrations.PowerShell;

/// <summary>
/// Manages TaskNotify PowerShell module installation into user's profile.
///
/// Per AGENTS.md 8.2:
/// - Uses TASKNOTIFY-BEGIN/END markers
/// - Preserves user's existing profile content
/// - Preserves Oh My Posh, Starship, Conda, PSReadLine
/// - Does NOT read or save full history
/// - Uninstall only removes TaskNotify-marked block
/// </summary>
public static class PowerShellProfileInstaller
{
    private const string BeginMarker = "# TASKNOTIFY-BEGIN";
    private const string EndMarker = "# TASKNOTIFY-END";
    private static readonly Regex BlockPattern = new(
        $"{BeginMarker}.*?{EndMarker}",
        RegexOptions.Singleline | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Installs TaskNotify into the user's PowerShell profile.
    /// Returns true if the profile was modified.
    /// </summary>
    public static bool Install(string? profilePath = null, string? modulePath = null)
    {
        profilePath ??= GetDefaultProfilePath();
        if (string.IsNullOrWhiteSpace(profilePath)) return false;

        var installBlock = CreateInstallBlock(EnsureModuleInstalled(modulePath));
        var existing = File.Exists(profilePath) ? File.ReadAllText(profilePath) : string.Empty;

        if (existing.Contains(BeginMarker) && existing.Contains(EndMarker))
        {
            var updated = BlockPattern.Replace(existing, installBlock);
            if (updated == existing) return false;
            Backup(profilePath);
            WriteProfile(profilePath, updated);
            return true;
        }

        Backup(profilePath);

        var newContent = existing.TrimEnd() + Environment.NewLine + Environment.NewLine + installBlock + Environment.NewLine;
        WriteProfile(profilePath, newContent);
        return true;
    }

    /// <summary>
    /// Uninstalls TaskNotify from the user's PowerShell profile.
    /// Only removes the TaskNotify-marked block.
    /// </summary>
    public static bool Uninstall(string? profilePath = null)
    {
        profilePath ??= GetDefaultProfilePath();
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath)) return false;

        var backupPath = profilePath + ".tasknotify.bak";
        File.Copy(profilePath, backupPath, overwrite: true);

        var existing = File.ReadAllText(profilePath);
        var cleaned = BlockPattern.Replace(existing, string.Empty).TrimEnd();

        // Clean up excessive blank lines
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");

        WriteProfile(profilePath, cleaned);
        return true;
    }

    /// <summary>
    /// Checks if TaskNotify is installed in the profile.
    /// </summary>
    public static bool IsInstalled(string? profilePath = null)
    {
        profilePath ??= GetDefaultProfilePath();
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath)) return false;
        var content = File.ReadAllText(profilePath);
        return content.Contains(BeginMarker) && content.Contains(EndMarker);
    }

    private static string CreateInstallBlock(string modulePath)
    {
        return $@"{BeginMarker}
# TaskNotify PowerShell Integration
try {{
    Import-Module ""{modulePath}"" -ErrorAction SilentlyContinue
    Initialize-TaskNotifyIntegration
}} catch {{
    # TaskNotify module not available; silently skip
}}
{EndMarker}";
    }

    private static string EnsureModuleInstalled(string? customModulePath)
    {
        if (!string.IsNullOrWhiteSpace(customModulePath))
        {
            if (!File.Exists(customModulePath)) throw new FileNotFoundException("PowerShell 集成模块不存在。", customModulePath);
            return Path.GetFullPath(customModulePath);
        }

        var source = Path.Combine(AppContext.BaseDirectory, "integrations", "PowerShell", "TaskNotify.psm1");
        if (!File.Exists(source)) throw new FileNotFoundException("TaskNotify 发布目录中缺少 PowerShell 集成模块。", source);

        var destination = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskNotify", "integrations", "PowerShell", "TaskNotify.psm1");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        return destination;
    }

    private static void Backup(string profilePath)
    {
        if (File.Exists(profilePath)) File.Copy(profilePath, profilePath + ".tasknotify.bak", overwrite: true);
    }

    private static void WriteProfile(string profilePath, string content)
    {
        var directory = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = profilePath + ".tasknotify.tmp";
        File.WriteAllText(temporaryPath, content, System.Text.Encoding.UTF8);
        File.Move(temporaryPath, profilePath, overwrite: true);
    }

    private static string? GetDefaultProfilePath()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetEnvironmentVariable("USERPROFILE")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (home is null) return null;

        // Check for PowerShell 7+ profile first, fall back to Windows PowerShell
        var pwsh7 = Path.Combine(home, "Documents", "PowerShell", "Microsoft.PowerShell_profile.ps1");
        if (File.Exists(pwsh7)) return pwsh7;

        var pwsh5 = Path.Combine(home, "Documents", "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
        if (File.Exists(pwsh5)) return pwsh5;

        // Return whichever exists, or the PS5 path as default
        return File.Exists(pwsh7) ? pwsh7 : pwsh5;
    }
}
