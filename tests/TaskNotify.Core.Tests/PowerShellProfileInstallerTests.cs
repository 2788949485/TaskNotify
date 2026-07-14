using TaskNotify.Integrations.PowerShell;
using Xunit;

namespace TaskNotify.Core.Tests;

public sealed class PowerShellProfileInstallerTests
{
    [Fact]
    public void Install_and_uninstall_preserve_the_existing_profile()
    {
        var profilePath = Path.Combine(AppContext.BaseDirectory, $"profile-{Guid.NewGuid():N}.ps1");
        var modulePath = Path.Combine(AppContext.BaseDirectory, $"module-{Guid.NewGuid():N}.psm1");
        var original = "function prompt { 'custom prompt' }";
        File.WriteAllText(profilePath, original);
        File.WriteAllText(modulePath, "# test module");

        try
        {
            Assert.True(PowerShellProfileInstaller.Install(profilePath, modulePath));
            var installed = File.ReadAllText(profilePath);
            Assert.Contains(original, installed);
            Assert.Contains("# TASKNOTIFY-BEGIN", installed);
            Assert.Contains(modulePath, installed);

            var replacementModule = Path.Combine(AppContext.BaseDirectory, $"module-{Guid.NewGuid():N}.psm1");
            File.WriteAllText(replacementModule, "# replacement module");
            try
            {
                Assert.True(PowerShellProfileInstaller.Install(profilePath, replacementModule));
                Assert.Contains(replacementModule, File.ReadAllText(profilePath));
            }
            finally
            {
                File.Delete(replacementModule);
            }

            Assert.True(PowerShellProfileInstaller.Uninstall(profilePath));
            Assert.Equal(original, File.ReadAllText(profilePath));
        }
        finally
        {
            File.Delete(profilePath);
            File.Delete(modulePath);
            File.Delete(profilePath + ".tasknotify.bak");
        }
    }
}
