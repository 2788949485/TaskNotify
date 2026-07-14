using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaskNotify.Integrations.Claude;

/// <summary>Safely installs and removes TaskNotify's Claude Code hooks.</summary>
public static class ClaudeSettingsManager
{
    private const string CommandMarker = "--tasknotify-hook";
    private const string LegacyHookKey = "tasknotify";

    private static readonly string[] HookEvents =
    [
        "UserPromptSubmit",
        "Stop",
        "StopFailure",
        "PermissionRequest",
        "Notification"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    public static string DefaultSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string DefaultInstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskNotify", "integrations", "Claude");

    public static string DefaultHookScriptPath => Path.Combine(DefaultInstallDirectory, "hook-receiver.js");

    public static string EnsureHookScriptInstalled(string? customHookPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customHookPath))
        {
            if (!File.Exists(customHookPath)) throw new FileNotFoundException("Claude Hook 脚本不存在。", customHookPath);
            return Path.GetFullPath(customHookPath);
        }

        var source = Path.Combine(AppContext.BaseDirectory, "integrations", "Claude", "hook-receiver.js");
        if (!File.Exists(source)) throw new FileNotFoundException("TaskNotify 发布目录中缺少 Claude Hook 脚本。", source);

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

        var command = BuildCommand(scriptPath);
        hooks.Remove(LegacyHookKey);
        RemoveTaskNotifyHandlers(hooks, "SessionStart");
        foreach (var eventName in HookEvents) RemoveTaskNotifyHandlers(hooks, eventName);
        AddHook(hooks, "UserPromptSubmit", command);
        AddHook(hooks, "Stop", command);
        AddHook(hooks, "StopFailure", command);
        AddHook(hooks, "PermissionRequest", command);
        AddHook(hooks, "Notification", command, "idle_prompt");
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

        hooks.Remove(LegacyHookKey);
        RemoveTaskNotifyHandlers(hooks, "SessionStart");
        foreach (var eventName in HookEvents)
        {
            RemoveTaskNotifyHandlers(hooks, eventName);
        }

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
            return ReadSettings(settingsPath)["hooks"] is JsonObject hooks && ContainsTaskNotifyHook(hooks);
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
            ?? throw new JsonException("Claude settings.json 必须是 JSON 对象。");
    }

    private static string BuildCommand(string scriptPath) =>
        $"node \"{scriptPath.Replace("\"", "\\\"")}\" {CommandMarker}";

    private static void AddHook(JsonObject hooks, string eventName, string command, string? matcher = null)
    {
        var eventHooks = hooks[eventName] as JsonArray ?? new JsonArray();
        var group = new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
                ["timeout"] = 5
            })
        };
        if (matcher is not null) group["matcher"] = matcher;
        eventHooks.Add(group);
        hooks[eventName] = eventHooks;
    }

    private static bool ContainsTaskNotifyHook(JsonObject hooks)
    {
        if (hooks.ContainsKey(LegacyHookKey)) return true;
        return HookEvents.Any(eventName =>
            hooks[eventName] is JsonArray groups && groups
                .OfType<JsonObject>()
                .SelectMany(group => (group["hooks"] as JsonArray)?.OfType<JsonObject>() ?? [])
                .Any(IsTaskNotifyHandler));
    }

    private static bool HasCompleteInstallation(JsonObject hooks) =>
        HookEvents.All(eventName =>
            hooks[eventName] is JsonArray groups && groups
                .OfType<JsonObject>()
                .SelectMany(group => (group["hooks"] as JsonArray)?.OfType<JsonObject>() ?? [])
                .Any(IsTaskNotifyHandler));

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
