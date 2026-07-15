using FluentAssertions;
using Moq;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using Xunit;

namespace MSOSync.PluginTests.Loading;

public sealed class PluginDependencyResolverTests
{
    private static PluginManifest ManifestWithDeps(params string[] deps) => new()
    {
        Id = "test.plugin", Name = "T", Version = "1.0.0",
        MinHostVersion = "1.0.0", MaxHostVersion = "99.0.0",
        EntryAssembly = "T.dll", EntryType = "T.P",
        Author = "T", Description = "T",
        Dependencies = deps,
    };

    private static IPluginRegistry RegistryWith(string pluginId, PluginStatus status)
    {
        var mock = new Mock<IPluginRegistry>();
        mock.Setup(r => r.GetById(pluginId)).Returns(new PluginDescriptor
        {
            PluginId = pluginId, Name = pluginId, Version = "1.0.0",
            Status   = status,
        });
        return mock.Object;
    }

    [Fact]
    public void Resolve_NoDependencies_ReturnsNull()
    {
        var manifest  = ManifestWithDeps();
        var registry  = new Mock<IPluginRegistry>().Object;
        var result    = PluginDependencyResolver.Resolve(manifest, registry);
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_DependencyLoaded_ReturnsNull()
    {
        var manifest = ManifestWithDeps("dep.plugin");
        var registry = RegistryWith("dep.plugin", PluginStatus.Loaded);
        var result   = PluginDependencyResolver.Resolve(manifest, registry);
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_DependencyNotFound_ReturnsError()
    {
        var manifest = ManifestWithDeps("dep.plugin");
        var mock     = new Mock<IPluginRegistry>();
        mock.Setup(r => r.GetById("dep.plugin")).Returns((PluginDescriptor?)null);
        var result   = PluginDependencyResolver.Resolve(manifest, mock.Object);
        result.Should().NotBeNull().And.Contain("dep.plugin");
    }

    [Fact]
    public void Resolve_DependencyFailed_ReturnsError()
    {
        var manifest = ManifestWithDeps("dep.plugin");
        var registry = RegistryWith("dep.plugin", PluginStatus.Failed);
        var result   = PluginDependencyResolver.Resolve(manifest, registry);
        result.Should().NotBeNull().And.Contain("dep.plugin");
    }
}
