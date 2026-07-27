using MSOSync.Cli.Scaffolding;
using Xunit;

namespace MSOSync.CliTests.Commands;

public sealed class PluginNewCommandTests : IDisposable
{
    private readonly string _tempDir;

    public PluginNewCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── Name conversion ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("acme.my-router",      "Acme.MyRouter",        "MyRouterPlugin")]
    [InlineData("company.sql-router",  "Company.SqlRouter",    "SqlRouterPlugin")]
    [InlineData("org.a.b.plugin",      "Org.A.B.Plugin",       "PluginPlugin")]
    [InlineData("x.y",                 "X.Y",                  "YPlugin")]
    [InlineData("acme.sql-collector",  "Acme.SqlCollector",    "SqlCollectorPlugin")]
    public void DeriveNames_ReturnsCorrectAssemblyAndClass(
        string pluginId, string expectedAssembly, string expectedClass)
    {
        (string assembly, string className) = PluginScaffolder.DeriveNames(pluginId);
        Assert.Equal(expectedAssembly, assembly);
        Assert.Equal(expectedClass, className);
    }

    // ── ID validation ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("acme.myrouter",       true)]
    [InlineData("company.sql-router",  true)]
    [InlineData("x.y.z",              true)]
    [InlineData("a1.b2",              true)]
    [InlineData("",                   false)]
    [InlineData("MyPlugin",           false)]    // uppercase
    [InlineData("acme myrouter",      false)]    // space
    [InlineData(".starts-with-dot",   false)]
    [InlineData("1starts-with-digit", false)]
    public void IsValidId_ReturnsExpected(string id, bool expected)
    {
        Assert.Equal(expected, PluginScaffolder.IsValidId(id));
    }

    // ── Scaffold — success ───────────────────────────────────────────────────

    [Fact]
    public async Task Scaffold_CreatesAllFourFiles_OnValidId()
    {
        string outputDir = Path.Combine(_tempDir, "acme.my-router");
        var    cmd       = new Cli.Commands.PluginNewCommand();

        int exitCode = await cmd.ExecuteAsync("acme.my-router", outputDir, "Acme", "My router plugin");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outputDir, "Acme.MyRouter.csproj")));
        Assert.True(File.Exists(Path.Combine(outputDir, "MyRouterPlugin.cs")));
        Assert.True(File.Exists(Path.Combine(outputDir, "plugin.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "plugin.config.json")));
    }

    [Fact]
    public async Task Scaffold_InjectsPluginIdIntoPluginJson()
    {
        string outputDir = Path.Combine(_tempDir, "org.checker");
        var    cmd       = new Cli.Commands.PluginNewCommand();

        await cmd.ExecuteAsync("org.checker", outputDir, "Org", "Checker plugin");

        string json = await File.ReadAllTextAsync(Path.Combine(outputDir, "plugin.json"));
        Assert.Contains("\"org.checker\"", json);
        Assert.Contains("Org.Checker.dll", json);
        Assert.Contains("Org.Checker.CheckerPlugin", json);
    }

    [Fact]
    public async Task Scaffold_InjectsAuthorAndDescriptionIntoPluginJson()
    {
        string outputDir = Path.Combine(_tempDir, "acme.ext");
        var    cmd       = new Cli.Commands.PluginNewCommand();

        await cmd.ExecuteAsync("acme.ext", outputDir, "Acme Corp", "Extension plugin");

        string json = await File.ReadAllTextAsync(Path.Combine(outputDir, "plugin.json"));
        Assert.Contains("Acme Corp", json);
        Assert.Contains("Extension plugin", json);
    }

    [Fact]
    public async Task Scaffold_InjectsNamespaceIntoCs()
    {
        string outputDir = Path.Combine(_tempDir, "acme.my-router2");
        var    cmd       = new Cli.Commands.PluginNewCommand();

        await cmd.ExecuteAsync("acme.my-router2", outputDir, string.Empty, string.Empty);

        string cs = await File.ReadAllTextAsync(Path.Combine(outputDir, "MyRouter2Plugin.cs"));
        Assert.Contains("namespace Acme.MyRouter2;", cs);
        Assert.Contains("class MyRouter2Plugin", cs);
    }

    // ── Scaffold — failure paths ─────────────────────────────────────────────

    [Fact]
    public async Task Scaffold_Returns2_OnInvalidId()
    {
        var cmd = new Cli.Commands.PluginNewCommand();
        int exitCode = await cmd.ExecuteAsync("Invalid.Plugin", Path.Combine(_tempDir, "out"), "", "");
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task Scaffold_Returns1_WhenDirectoryAlreadyExists()
    {
        string outputDir = Path.Combine(_tempDir, "acme.dup");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "existing.txt"), "content");

        var cmd      = new Cli.Commands.PluginNewCommand();
        int exitCode = await cmd.ExecuteAsync("acme.dup", outputDir, "", "");
        Assert.Equal(1, exitCode);
    }
}
