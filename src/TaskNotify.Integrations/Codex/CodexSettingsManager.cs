using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaskNotify.Integrations.Codex;

/// <summary>Safely installs and removes TaskNotify's user-level Codex hooks.</summary>
public static class CodexSettingsManager
{
    private const string CommandMarker = "--tasknotify-codex-hook";

    private static readonly string[] HookEvents =
    [
        "UserPromptSubmit",
        "PermissionRequest",
        "Stop"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    public static string DefaultSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "hooks.json");

    public static string DefaultInstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskNotify", "integrations", "Codex");

    public static string DefaultHookScriptPath => Path.Combine(DefaultInstallDirectory, "hook-receiver.js");

    public static string EnsureHookScriptInstalled(string? customHookPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customHookPath))
        {
            if (!File.Exists(customHookPath)) throw new FileNotFoundException("Codex Hook 脚本不存在。", customHookPath);
            return Path.GetFullPath(customHookPath);
        }

        var source = Path.Combine(AppContext.BaseDirectory, "integrations", "Codex", "hook-receiver.js");
        if (!File.Exists(source)) throw new FileNotFoundException("TaskNotify 发布目录中缺少 Codex Hook 脚本。", source);

        Directory.CreateDirectory(DefaultInstallDirectory);
        File.Copy(source, DefaultHookScriptPath, overwrite: true);
        return DefaultHookScriptPath;
    }

    public static bool Install(string? settingsPath = null, string? hookScriptPath = null)
    {
        settingsPath = string.IsNullOrWhiteSpace(settingsPath) ? DefaultSettingsPath : settingsPath;
        var settings = ReadSettings(settingsPath);
        var hooks = settings["hooks"] as JsonObject ?? new JsonObject();
        var scriptPath = EnsureHookScriptInstalled(hookScriptPath);

        if (HasCompleteInstallation(hooks)) return false;

        foreach (var eventName in HookEvents) RemoveTaskNotifyHandlers(hooks, eventName);
        var command = $"node \"{scriptPath.Replace("\"", "\\\"")}\" {CommandMarker}";
        foreach (var eventName in HookEvents) AddHook(hooks, eventName, command);
        settings["hooks"] = hooks;

        Backup(settingsPath);
        WriteSettings(settingsPath, settings);
        return true;
    }

    public static bool Uninstall(string? settingsPath = null)
    {
        settingsPath = string.IsNullOrWhiteSpace(settingsPath) ? DefaultSettingsPath : settingsPath;
        if (!File.Exists(settingsPath)) return false;

        var settings = ReadSettings(settingsPath);
        if (settings["hooks"] is not JsonObject hooks || !ContainsTaskNotifyHook(hooks)) return false;

        foreach (var eventName in HookEvents) RemoveTaskNotifyHandlers(hooks, eventName);
        if (hooks.Count == 0) settings.Remove("hooks");
        Backup(settingsPath);
        WriteSettings(settingsPath, settings);
        return true;
    }

    public static bool IsInstalled(string? settingsPath = null)
    {
        settingsPath = string.IsNullOrWhiteSpace(settingsPath) ? DefaultSettingsPath : settingsPath;
        if (!File.Exists(settingsPath)) return false;
        try
        {
            return ReadSettings(settingsPath)["hooks"] is JsonObject hooks && HasCompleteInstallation(hooks);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonObject ReadSettings(string settingsPath)
    {
        if (!File.Exists(settingsPath)) return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject()
            ?? throw new JsonException("Codex hooks.json 必须是 JSON 对象。");
    }

    private static void AddHook(JsonObject hooks, string eventName, string command)
    {
        var eventHooks = hooks[eventName] as JsonArray ?? new JsonArray();
        eventHooks.Add(new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
                ["timeout"] = 5
            })
        });
        hooks[eventName] = eventHooks;
    }

    private static bool HasCompleteInstallation(JsonObject hooks) =>
        HookEvents.All(eventName => HasTaskNotifyHandler(hooks[eventName] as JsonArray));

    private static bool ContainsTaskNotifyHook(JsonObject hooks) =>
        HookEvents.Any(eventName => HasTaskNotifyHandler(hooks[eventName] as JsonArray));

    private static bool HasTaskNotifyHandler(JsonArray? groups) =>
        groups?.OfType<JsonObject>()
            .SelectMany(group => (group["hooks"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Any(IsTaskNotifyHandler) == true;

    private static void RemoveTaskNotifyHandlers(JsonObject hooks, string eventName)
    {
        if (hooks[eventName] is not JsonArray groups) return;
        foreach (var group in groups.OfType<JsonObject>().ToArray())
        {
            if (group["hooks"] is not JsonArray handlers) continue;
            foreach (var handler in handlers.OfType<JsonObject>().Where(IsTaskNotifyHandler).ToArray())
            {
                handlers.Remove(handler);
            }
            if (handlers.Count == 0) groups.Remove(group);
        }
        if (groups.Count == 0) hooks.Remove(eventName);
    }

    private static bool IsTaskNotifyHandler(JsonObject handler) =>
        handler["type"]?.GetValue<string>() == "command" &&
        handler["command"]?.GetValue<string>().Contains(CommandMarker, StringComparison.Ordinal) == true;

    private static void Backup(string settingsPath)
    {
        if (!File.Exists(settingsPath)) return;
        File.Copy(settingsPath, $"{settingsPath}.bak.{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
    }

    private static void WriteSettings(string settingsPath, JsonObject settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!);
        var tempPath = $"{settingsPath}.tmp.{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(tempPath, settings.ToJsonString(JsonOptions), new UTF8Encoding(false));
            File.Move(tempPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
