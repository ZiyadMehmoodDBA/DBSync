using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.ConfigurationTests;
using MSOSync.Metadata.Configuration;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ConfigurationTests.Services;

public sealed class ConfigurationValidationServiceTests : IClassFixture<ConfigurationDbFixture>
{
    private readonly ConfigurationDbFixture _fx;
    private readonly IConfigurationValidationService _svc;

    public ConfigurationValidationServiceTests(ConfigurationDbFixture fx)
    {
        _fx = fx;
        _svc = new ConfigurationValidationService(fx.Db);
    }

    private static ConfigurationSettings Valid(
        List<Guid>? channels = null,
        List<Guid>? routers  = null,
        List<Guid>? triggers = null) => new()
    {
        HeartbeatIntervalSeconds = 30,
        TransportMode            = "Push",
        MaxRetryAttempts         = 3,
        RetryBackoffSeconds      = 60,
        BatchSizeLimit           = 1000,
        FeatureFlags             = new() { [FeatureFlagCatalog.EnableBulkApply] = true },
        ChannelIds               = channels ?? [],
        RouterIds                = routers  ?? [],
        TriggerIds               = triggers ?? [],
    };

    [Fact]
    public async Task Valid_Settings_PassesGate()
    {
        var result = await _svc.ValidateAsync(Valid(), CancellationToken.None);
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public async Task Rule6_HeartbeatInterval_OutOfRange_Fails(int seconds)
    {
        var result = await _svc.ValidateAsync(
            Valid() with { HeartbeatIntervalSeconds = seconds }, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "HeartbeatIntervalSeconds");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task Rule7_MaxRetryAttempts_OutOfRange_Fails(int v)
    {
        var result = await _svc.ValidateAsync(
            Valid() with { MaxRetryAttempts = v }, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "MaxRetryAttempts");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public async Task Rule8_BatchSizeLimit_OutOfRange_Fails(int v)
    {
        var result = await _svc.ValidateAsync(
            Valid() with { BatchSizeLimit = v }, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "BatchSizeLimit");
    }

    [Fact]
    public async Task Rule10_UnknownFeatureFlag_Fails()
    {
        var result = await _svc.ValidateAsync(
            Valid() with { FeatureFlags = new() { ["unknownFlag"] = true } }, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "FeatureFlags");
    }

    [Fact]
    public async Task Rule11_InvalidTransportMode_Fails()
    {
        var result = await _svc.ValidateAsync(
            Valid() with { TransportMode = "FTP" }, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "TransportMode");
    }

    [Fact]
    public async Task Rule1_MissingChannelId_Fails()
    {
        var missingId = Guid.NewGuid();
        var result = await _svc.ValidateAsync(
            Valid(channels: [missingId]), CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "ChannelIds");
    }

    [Fact]
    public async Task Rule4_DisabledChannel_Fails()
    {
        // Seed a disabled channel whose ID matches what settings references
        var channelId = Guid.NewGuid();
        _fx.Db.Channels.Add(new SyncChannel
        {
            ChannelId = channelId.ToString(),
            Priority  = 1,
            Enabled   = false,
        });
        await _fx.Db.SaveChangesAsync();

        var result = await _svc.ValidateAsync(
            Valid(channels: [channelId]), CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Field == "ChannelIds" && e.Message.Contains("disabled"));
    }

    [Fact]
    public async Task Rule12_SchemaVersionExceedsMax_Fails()
    {
        var result = await _svc.ValidateAsync(Valid(), CancellationToken.None, schemaVersion: 999);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "SchemaVersion");
    }
}
