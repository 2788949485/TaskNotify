using System.Text.Json.Nodes;
using TaskNotify.Integrations.Claude;
using Xunit;

namespace TaskNotify.Core.Tests;

public sealed class ClaudeSettingsManagerTests
{
    [Fact]
    public void Install_and_uninstall_preserve_existing_hooks()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"claude-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(directory, "settings.json");
        var hookPath = Path.Combine(directory, "hook-receiver.js");
        Directory.CreateDirectory(directory);
        File.WriteAllText(hookPath, "process.exit(0);");
        File.WriteAllText(settingsPath, """
            {
              "theme": "dark",
              "hooks": {
                "Stop": [{ "hooks": [{ "type": "command", "command": "existing-hook" }] }],
                "PreToolUse": [{ "matcher": "Bash", "hooks": [{ "type": "command", "command": "lint" }] }]
              }
            }
            """);

        try
        {
            Assert.True(ClaudeSettingsManager.Install(settingsPath, hookPath));
            Assert.True(ClaudeSettingsManager.IsInstalled(settingsPath));
            Assert.False(ClaudeSettingsManager.Install(settingsPath, hookPath));

            var installed = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
            Assert.Equal("dark", installed["theme"]!.GetValue<string>());
            Assert.NotNull(installed["hooks"]!["PreToolUse"]);
            Assert.Contains("existing-hook", installed["hooks"]!["Stop"]!.ToJsonString());
            Assert.NotNull(installed["hooks"]!["UserPromptSubmit"]);
            Assert.Null(installed["hooks"]!["SessionStart"]);
            Assert.Null(installed["hooks"]!["tasknotify"]);

            Assert.True(ClaudeSettingsManager.Uninstall(settingsPath));
            Assert.False(ClaudeSettingsManager.IsInstalled(settingsPath));

            var uninstalled = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
            Assert.Equal("dark", uninstalled["theme"]!.GetValue<string>());
            Assert.NotNull(uninstalled["hooks"]!["PreToolUse"]);
            Assert.Contains("existing-hook", uninstalled["hooks"]!["Stop"]!.ToJsonString());
            Assert.Equal(2, Directory.GetFiles(directory, "settings.json.bak.*").Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
