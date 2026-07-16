using FluentAssertions;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Models;
using Xunit;

namespace MSOSync.PluginTests.Lifecycle;

public sealed class SdkCompatibilityValidatorTests
{
    private static SdkCompatibilityValidator Make(string sdkMajor = "1", string apiVersion = "1")
        => new(Options.Create(new PluginHostOptions
        {
            SupportedSdkMajorVersion = sdkMajor,
            SupportedApiVersion      = apiVersion
        }));

    private static PluginManifest Manifest(string sdkVer, string apiVer)
        => new()
        {
            Id = "test", Name = "T", Version = "1.0.0", SdkVersion = sdkVer,
            ApiVersion = apiVer, MinHostVersion = "1.0.0", MaxHostVersion = "99.9.999",
            EntryAssembly = "T.dll", EntryType = "T.T", Author = "A", Description = "D"
        };

    [Fact]
    public void Validate_MatchingSdkAndApi_ReturnsCompatible()
    {
        var result = Make().Validate(Manifest("1.0", "1"), out var msg);
        result.Should().Be(CompatibilityResult.Compatible);
        msg.Should().BeNull();
    }

    [Fact]
    public void Validate_SdkMajorMismatch_ReturnsIncompatible()
    {
        var result = Make(sdkMajor: "1").Validate(Manifest("2.0", "1"), out var msg);
        result.Should().Be(CompatibilityResult.Incompatible);
        msg.Should().Contain("sdkVersion");
    }

    [Fact]
    public void Validate_ApiVersionMismatch_ReturnsIncompatible()
    {
        var result = Make(apiVersion: "1").Validate(Manifest("1.0", "2"), out var msg);
        result.Should().Be(CompatibilityResult.Incompatible);
        msg.Should().Contain("apiVersion");
    }

    [Fact]
    public void Validate_SdkMinorVersionDiffers_StillCompatible()
    {
        // 1.5 has same major (1) as supported major (1)
        var result = Make().Validate(Manifest("1.5", "1"), out _);
        result.Should().Be(CompatibilityResult.Compatible);
    }
}
