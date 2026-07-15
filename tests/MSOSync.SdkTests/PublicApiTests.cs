using FluentAssertions;
using MSOSync.Sdk.Abstractions;
using Xunit;

namespace MSOSync.SdkTests;

public sealed class PublicApiTests
{
    [Fact]
    public void MSOSync_Sdk_PublicApiSurface_MatchesSnapshot()
    {
        var assembly    = typeof(IPlugin).Assembly;
        var publicTypes = assembly
            .GetExportedTypes()
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => t.FullName!)
            .ToList();

        var expected = new[]
        {
            "MSOSync.Sdk.Abstractions.IPlugin",
            "MSOSync.Sdk.Abstractions.IPluginConfiguration",
            "MSOSync.Sdk.Abstractions.IPluginContext",
            "MSOSync.Sdk.Abstractions.IPluginEnvironment",
            "MSOSync.Sdk.Abstractions.IPluginLogger",
            "MSOSync.Sdk.Abstractions.IPluginServices",
            "MSOSync.Sdk.Hosting.PluginBase",
            "MSOSync.Sdk.Metadata.PluginCapability",
            "MSOSync.Sdk.Metadata.PluginMetadata",
            "MSOSync.Sdk.Metadata.PluginPermission",
        };

        publicTypes.Should().BeEquivalentTo(expected,
            "the public API surface must not change without updating this snapshot");
    }
}
