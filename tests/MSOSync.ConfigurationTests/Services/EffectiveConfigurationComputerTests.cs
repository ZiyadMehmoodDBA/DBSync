using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.ConfigurationTests;
using MSOSync.Metadata.Configuration;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Models;
using System.Text.Json;
using Xunit;

namespace MSOSync.ConfigurationTests.Services;

public sealed class EffectiveConfigurationComputerTests : IClassFixture<ConfigurationDbFixture>
{
    private readonly ConfigurationDbFixture _fx;
    private readonly IEffectiveConfigurationComputer _svc;
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public EffectiveConfigurationComputerTests(ConfigurationDbFixture fx)
    {
        _fx  = fx;
        _svc = new EffectiveConfigurationComputer(fx.Db,
            new ConfigurationValidationService(fx.Db));
    }

    private SyncConfigurationTemplateVersion MakeVersion(ConfigurationSettings s) => new()
    {
        Id            = Guid.NewGuid(),
        TemplateId    = Guid.NewGuid(),
        VersionNumber = 1,
        IsDraft       = false,
        SettingsJson  = JsonSerializer.Serialize(s, _json),
        SchemaVersion = 1,
    };

    private ConfigurationSettings BaseSettings() => new()
    {
        HeartbeatIntervalSeconds = 30,
        TransportMode = "Push",
        MaxRetryAttempts = 3,
        RetryBackoffSeconds = 60,
        BatchSizeLimit = 1000,
        FeatureFlags = [],
        ChannelIds   = [],
        RouterIds    = [],
        TriggerIds   = [],
    };

    [Fact]
    public async Task NoOverrides_EffectiveHashMatchesTemplateSettings()
    {
        var version  = MakeVersion(BaseSettings());
        var expected = CanonicalJsonSerializer.ComputeHash(BaseSettings());

        var result = await _svc.ComputeAsync(version, "node-no-override", CancellationToken.None);

        result.EffectiveHash.Should().Be(expected);
        result.Settings.HeartbeatIntervalSeconds.Should().Be(30);
    }

    [Fact]
    public async Task SingleOverride_ChangesEffectiveHash()
    {
        var version = MakeVersion(BaseSettings());

        // Add override: BatchSizeLimit override from "1000" → "500"
        var nodeId = "node-with-override";
        _fx.Db.NodeConfigurationOverrides.Add(new SyncNodeConfigurationOverride
        {
            Id             = Guid.NewGuid(),
            NodeId         = nodeId,
            SettingKey     = "batchSizeLimit",
            SettingValue   = "500",
            OverrideSource = "Manual",
            UpdatedBy      = Guid.NewGuid(),
            UpdatedAt      = DateTime.UtcNow,
        });
        await _fx.Db.SaveChangesAsync();

        var result = await _svc.ComputeAsync(version, nodeId, CancellationToken.None);

        result.Settings.BatchSizeLimit.Should().Be(500);
        result.EffectiveHash.Should().NotBe(CanonicalJsonSerializer.ComputeHash(BaseSettings()));
    }

    [Fact]
    public async Task OverrideDoesNotAffectTemplateContentHash()
    {
        var version  = MakeVersion(BaseSettings());
        var expected = CanonicalJsonSerializer.ComputeHash(BaseSettings());

        // Template content hash = hash of settings without any overrides
        // The EffectiveConfigComputer should not mutate TemplateContentHash
        version.TemplateContentHash = expected;

        var nodeId = "node-override-hash";
        _fx.Db.NodeConfigurationOverrides.Add(new SyncNodeConfigurationOverride
        {
            Id = Guid.NewGuid(), NodeId = nodeId,
            SettingKey = "maxRetryAttempts", SettingValue = "10",
            OverrideSource = "Manual", UpdatedBy = Guid.NewGuid(), UpdatedAt = DateTime.UtcNow,
        });
        await _fx.Db.SaveChangesAsync();

        await _svc.ComputeAsync(version, nodeId, CancellationToken.None);

        // TemplateContentHash must be unchanged
        version.TemplateContentHash.Should().Be(expected);
    }
}
