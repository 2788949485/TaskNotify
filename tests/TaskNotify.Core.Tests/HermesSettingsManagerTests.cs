using TaskNotify.Integrations.Hermes;
using Xunit;

namespace TaskNotify.Core.Tests;

public sealed class HermesSettingsManagerTests
{
    [Fact]
    public void Install_preserves_existing_config_and_is_idempotent()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"hermes-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(directory, "config.yaml");
        var hookPath = Path.Combine(directory, "hook-receiver.js");
        Directory.CreateDirectory(directory);
        File.WriteAllText(settingsPath, "model: test-model\n");
        File.WriteAllText(hookPath, "process.stdout.write('{}\\n');");

        try
        {
            Assert.True(HermesSettingsManager.Install(settingsPath, hookPath));
            Assert.True(HermesSettingsManager.IsInstalled(settingsPath));
            Assert.False(HermesSettingsManager.Install(settingsPath, hookPath));
            var installed = File.ReadAllText(settingsPath);
            Assert.Contains("model: test-model", installed);
            Assert.Contains("pre_llm_call:", installed);
            Assert.Contains("post_llm_call:", installed);
            Assert.Single(Directory.GetFiles(directory, "config.yaml.bak.*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
