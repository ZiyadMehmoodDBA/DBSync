using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using Xunit;

namespace MSOSync.PluginTests.Loading;

public sealed class PluginLoaderTests : IDisposable
{
    private readonly string _pluginsRoot = Path.Combine(Path.GetTempPath(), "loader-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<PluginLoader> _loaders = [];

    public PluginLoaderTests() => Directory.CreateDirectory(_pluginsRoot);

    public void Dispose()
    {
        // Unload all AssemblyLoadContexts before deleting temp directories (required on Windows)
        foreach (var loader in _loaders)
        {
            foreach (var ctx in loader.LoadContexts)
                ctx.Unload();
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();

        try { Directory.Delete(_pluginsRoot, true); }
        catch (UnauthorizedAccessException) { /* lock may not be released yet — temp dir will be cleaned on next GC */ }
    }

    private PluginLoader MakeLoader(IPluginStore? store = null)
    {
        if (store == null)
        {
            var defaultMock = new Mock<IPluginStore>();
            defaultMock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<PluginRecord>());
            defaultMock.Setup(s => s.UpsertAsync(It.IsAny<PluginRecord>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            store = defaultMock.Object;
        }

        var scopeFactory = new Mock<IServiceScopeFactory>();
        var scope        = new Mock<IServiceScope>();
        var provider     = new Mock<IServiceProvider>();

        provider.Setup(p => p.GetService(typeof(IPluginStore))).Returns(store);
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var loader = new PluginLoader(
            new PluginRegistry(),
            scopeFactory.Object,
            Options.Create(new PluginHostOptions { PluginsPath = _pluginsRoot, HostVersion = "14.0.0" }),
            NullLogger<PluginLoader>.Instance);
        _loaders.Add(loader);
        return loader;
    }

    private string CreatePluginDir(string dirName, string pluginId,
        bool createDll = true, string? version = "1.0.0",
        string? minHost = "1.0.0", string? maxHost = "99.9.999",
        string? entryType = "Test.Plugin", bool writeBadJson = false)
    {
        var dir = Path.Combine(_pluginsRoot, dirName);
        Directory.CreateDirectory(dir);

        if (writeBadJson)
        {
            File.WriteAllText(Path.Combine(dir, "plugin.json"), "{ invalid json {{");
            return dir;
        }

        var manifest = $$"""
            {
              "id": "{{pluginId}}",
              "name": "{{pluginId}}",
              "version": "{{version}}",
              "minHostVersion": "{{minHost}}",
              "maxHostVersion": "{{maxHost}}",
              "entryAssembly": "Test.dll",
              "entryType": "{{entryType}}",
              "author": "Test",
              "description": "Test plugin"
            }
            """;
        File.WriteAllText(Path.Combine(dir, "plugin.json"), manifest);

        if (createDll)
        {
            // Copy a real assembly as a stand-in (loader verifies it exists; entryType will be missing)
            var src = typeof(PluginLoaderTests).Assembly.Location;
            File.Copy(src, Path.Combine(dir, "Test.dll"), overwrite: true);
        }

        return dir;
    }

    [Fact]
    public async Task LoadAllAsync_EmptyPluginsDir_ReturnsEmpty()
    {
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAllAsync_MissingPluginsDir_ReturnsEmpty()
    {
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(Path.Combine(_pluginsRoot, "no-such-dir"), default);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAllAsync_BadManifestJson_ReturnsFailed()
    {
        CreatePluginDir("bad-json-plugin", "bad.plugin", writeBadJson: true);
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results.Should().HaveCount(1);
        results[0].Outcome.Should().Be(PluginLoadOutcome.Failed);
        results[0].FailureStage.Should().Be("Parse");
    }

    [Fact]
    public async Task LoadAllAsync_IncompatibleHostVersion_ReturnsFailed()
    {
        CreatePluginDir("compat-plugin", "compat.plugin",
            minHost: "99.0.0", maxHost: "99.9.999");
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results[0].Outcome.Should().Be(PluginLoadOutcome.Failed);
        results[0].FailureStage.Should().Be("HostCompatibility");
    }

    [Fact]
    public async Task LoadAllAsync_DisabledPlugin_ReturnsDisabled()
    {
        CreatePluginDir("disabled-plugin", "disabled.plugin");
        var storeMock = new Mock<IPluginStore>();
        storeMock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PluginRecord>
            {
                new() { PluginId = "disabled.plugin", PluginName = "n", PluginVersion = "1.0.0",
                        Status = "Disabled", Enabled = false, InstalledAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow }
            });
        storeMock.Setup(s => s.UpsertAsync(It.IsAny<PluginRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loader  = MakeLoader(storeMock.Object);
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results[0].Outcome.Should().Be(PluginLoadOutcome.Disabled);
    }

    [Fact]
    public async Task LoadAllAsync_EntryTypeNotFound_ReturnsFailed()
    {
        // Uses the test assembly as "Test.dll" but specifies a non-existent entry type
        CreatePluginDir("bad-type-plugin", "bad.type.plugin",
            entryType: "This.Type.Does.Not.Exist.AtAll");
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results[0].Outcome.Should().Be(PluginLoadOutcome.Failed);
        results[0].FailureStage.Should().Be("EntryTypeVerification");
    }

    [Fact]
    public async Task LoadAllAsync_MissingDll_ReturnsFailed()
    {
        CreatePluginDir("no-dll-plugin", "no.dll.plugin", createDll: false);
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results[0].Outcome.Should().Be(PluginLoadOutcome.Failed);
        results[0].FailureStage.Should().Be("ManifestValidation");
    }
}
