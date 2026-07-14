using System.Text.Json.Nodes;
using TaskNotify.Integrations.Codex;
using Xunit;

namespace TaskNotify.Core.Tests;

public sealed class CodexSettingsManagerTests
{
    [Fact]
    public void Install_and_uninstall_preserve_existing_hooks()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"codex-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(directory, "hooks.json");
        var hookPath = Path.Combine(directory, "hook-receiver.js");
        Directory.CreateDirectory(directory);
        File.WriteAllText(hookPath, "process.exit(0);");
        File.WriteAllText(settingsPath, """
            {
              "hooks": {
                "Stop": [{ "hooks": [{ "type": "command", "command": "existing-hook" }] }],
                "PreToolUse": [{ "matcher": "Bash", "hooks": [{ "type": "command", "command": "policy" }] }]
              }
            }
            """);

        try
        {
            Assert.True(CodexSettingsManager.Install(settingsPath, hookPath));
            Assert.True(CodexSettingsManager.IsInstalled(settingsPath));
            Assert.False(CodexSettingsManager.Install(settingsPath, hookPath));

            var installed = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
            Assert.NotNull(installed["hooks"]!["PreToolUse"]);
            Assert.Contains("existing-hook", installed["hooks"]!["Stop"]!.ToJsonString());
            Assert.NotNull(installed["hooks"]!["UserPromptSubmit"]);
            Assert.NotNull(installed["hooks"]!["PermissionRequest"]);

            Assert.True(CodexSettingsManager.Uninstall(settingsPath));
            Assert.False(CodexSettingsManager.IsInstalled(settingsPath));
            var uninstalled = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
            Assert.NotNull(uninstalled["hooks"]!["PreToolUse"]);
            Assert.Contains("existing-hook", uninstalled["hooks"]!["Stop"]!.ToJsonString());
            Assert.Equal(2, Directory.GetFiles(directory, "hooks.json.bak.*").Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
