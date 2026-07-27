using MSOSync.Cli.Commands;
using MSOSync.Cli.Packaging;
using Xunit;

namespace MSOSync.CliTests.Commands;

/// <summary>
/// Tests PluginPackCommand.ExecuteAsync validation paths
/// (delegates to PluginPacker — build pipeline tests live in PluginPackerTests).
/// </summary>
public sealed class PluginPackCommandTests
{
    [Fact]
    public async Task ExecuteAsync_Returns2_WhenNoPluginJson()
    {
        string workDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(workDir);
        try
        {
            var cmd      = new PluginPackCommand();
            int exitCode = await cmd.ExecuteAsync(workDir, "artifacts", "Release", null);
            Assert.Equal(2, exitCode);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Returns2_WhenManifestInvalid()
    {
        string workDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(workDir);
        try
        {
            // version field missing
            File.WriteAllText(Path.Combine(workDir, "plugin.json"),
                """{"id":"acme.test","name":"Test","entryAssembly":"T.dll","entryType":"T"}""");
            var cmd      = new PluginPackCommand();
            int exitCode = await cmd.ExecuteAsync(workDir, "artifacts", "Release", null);
            Assert.Equal(2, exitCode);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }
}
