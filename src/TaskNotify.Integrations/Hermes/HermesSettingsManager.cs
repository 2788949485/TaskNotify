using System.Text;
using System.Text.RegularExpressions;

namespace TaskNotify.Integrations.Hermes;

public static partial class HermesSettingsManager
{
    private const string BeginMarker = "# TASKNOTIFY-BEGIN";
    private const string EndMarker = "# TASKNOTIFY-END";
    private const string CommandMarker = "--tasknotify-hermes-hook";

    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hermes", "config.yaml");

    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskNotify", "integrations", "Hermes");

    public static string DefaultHookScriptPath => Path.Combine(DefaultInstallDirectory, "hook-receiver.js");

    public static bool Install(string? settingsPath = null, string? hookScriptPath = null)
    {
        settingsPath = string.IsNullOrWhiteSpace(settingsPath) ? DefaultSettingsPath : settingsPath;
        var scriptPath = EnsureHookScriptInstalled(hookScriptPath);
        var original = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : string.Empty;
        var block = BuildBlock(scriptPath);

        if (original.Contains(BeginMarker, StringComparison.Ordinal))
        {
            var updated = MarkerBlockRegex().Replace(original, block);
            if (updated == original) return false;
            Write(settingsPath, original, updated);
            return true;
        }

        // ponytail: refuse ambiguous YAML merging; use a YAML parser when existing Hermes hooks must be merged.
        if (TopLevelHooksRegex().IsMatch(original))
        {
            throw new InvalidOperationException("Hermes 已配置其他 hooks；TaskNotify 不会覆盖它们。");
        }

        var separator = original.Length == 0 || original.EndsWith('\n') ? string.Empty : Environment.NewLine;
        Write(settingsPath, original, original + separator + block + Environment.NewLine);
        return true;
    }

    public static bool IsInstalled(string? settingsPath = null)
    {
        settingsPath = string.IsNullOrWhiteSpace(settingsPath) ? DefaultSettingsPath : settingsPath;
        return File.Exists(settingsPath) && File.ReadAllText(settingsPath).Contains(CommandMarker, StringComparison.Ordinal);
    }

    private static string EnsureHookScriptInstalled(string? customHookPath)
    {
        if (!string.IsNullOrWhiteSpace(customHookPath))
        {
            if (!File.Exists(customHookPath)) throw new FileNotFoundException("Hermes Hook 脚本不存在。", customHookPath);
            return Path.GetFullPath(customHookPath);
        }

        var source = Path.Combine(AppContext.BaseDirectory, "integrations", "Hermes", "hook-receiver.js");
        if (!File.Exists(source)) throw new FileNotFoundException("TaskNotify 发布目录中缺少 Hermes Hook 脚本。", source);
        Directory.CreateDirectory(DefaultInstallDirectory);
        File.Copy(source, DefaultHookScriptPath, overwrite: true);
        return DefaultHookScriptPath;
    }

    private static string BuildBlock(string scriptPath)
    {
        var command = $"node \"{scriptPath.Replace("\"", "\\\"")}\" {CommandMarker}";
        return $$"""
            # TASKNOTIFY-BEGIN
            hooks:
              pre_llm_call:
                - command: '{{command.Replace("'", "''")}}'
                  timeout: 5
              post_llm_call:
                - command: '{{command.Replace("'", "''")}}'
                  timeout: 5
            # TASKNOTIFY-END
            """;
    }

    private static void Write(string settingsPath, string original, string updated)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!);
        if (File.Exists(settingsPath))
        {
            File.Copy(settingsPath, $"{settingsPath}.bak.{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        }

        var temp = $"{settingsPath}.tmp.{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temp, updated, new UTF8Encoding(false));
            File.Move(temp, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [GeneratedRegex(@"(?ms)^# TASKNOTIFY-BEGIN\r?\n.*?^# TASKNOTIFY-END")]
    private static partial Regex MarkerBlockRegex();

    [GeneratedRegex(@"(?m)^hooks\s*:")]
    private static partial Regex TopLevelHooksRegex();
}
