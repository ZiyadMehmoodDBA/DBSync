using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.ConfigurationTests;
using MSOSync.Metadata.Configuration;
using MSOSync.Metadata.Dtos;
using MSOSync.Persistence.Entities;
using System.Text.Json;
using Xunit;

namespace MSOSync.ConfigurationTests.Services;

public sealed class HeartbeatProcessorTests : IClassFixture<ConfigurationDbFixture>
{
    private readonly ConfigurationDbFixture _fx;
    private readonly HeartbeatProcessor _processor;
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public HeartbeatProcessorTests(ConfigurationDbFixture fx)
    {
        _fx = fx;
        var validSvc = new ConfigurationValidationService(fx.Db);
        var computer = new EffectiveConfigurationComputer(fx.Db, validSvc);
        var detector = new DriftDetector();
        _processor   = new HeartbeatProcessor(fx.Db, computer, detector);
    }

    private async Task<(SyncNode node, SyncConfigurationTemplateVersion version)> SeedNodeWithTemplate()
    {
        var settings = new ConfigurationSettings
        {
            HeartbeatIntervalSeconds = 30, TransportMode = "Push",
            MaxRetryAttempts = 3, RetryBackoffSeconds = 60, BatchSizeLimit = 1000,
            FeatureFlags = [], ChannelIds = [], RouterIds = [], TriggerIds = [],
        };
        var template = new SyncConfigurationTemplate
        {
            Id = Guid.NewGuid(), Name = $"HB-{Guid.NewGuid():N}", Status = "Published",
            CurrentPublishedVersion = 1, CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _fx.Db.ConfigurationTemplates.Add(template);

        var hash = CanonicalJsonSerializer.ComputeHash(settings);
        var version = new SyncConfigurationTemplateVersion
        {
            Id = Guid.NewGuid(), TemplateId = template.Id, VersionNumber = 1,
            IsDraft = false, SettingsJson = JsonSerializer.Serialize(settings, _json),
            TemplateContentHash = hash, SchemaVersion = 1,
        };
        _fx.Db.ConfigurationTemplateVersions.Add(version);

        var node = new SyncNode
        {
            NodeId = $"hb-node-{Guid.NewGuid():N}",
            GroupId = "g", SyncUrl = "http://x",
            AssignedTemplateId      = template.Id,
            AssignedTemplateVersion = 1,
            ExpectedEffectiveHash   = hash,
            ConfigurationState      = ConfigurationState.UpdateAvailable,
        };
        _fx.Db.Nodes.Add(node);
        await _fx.Db.SaveChangesAsync();

        return (node, version);
    }

    [Fact]
    public async Task Process_AppliedStatus_SetsCurrentState()
    {
        var (node, version) = await SeedNodeWithTemplate();
        var hash = version.TemplateContentHash!;

        var request = new HeartbeatRequest(
            NodeId: node.NodeId, NodeVersion: "1.0.0", UptimeSeconds: 100,
            DatabaseType: null, TransportMode: null,
            AppliedTemplateVersion: 1,
            AppliedEffectiveHash: hash,
            ConfigurationApplyStatus: ConfigurationApplyStatus.Applied);

        var response = await _processor.ProcessAsync(node.NodeId, request, CancellationToken.None);

        response.ConfigurationState.Should().Be(ConfigurationState.Current);
        response.AssignedTemplateId.Should().Be(node.AssignedTemplateId);
        response.AssignedTemplateVersion.Should().Be(1);
        response.ContentHash.Should().Be(hash);
    }

    [Fact]
    public async Task Process_HashMismatch_SetsDriftedState()
    {
        var (node, version) = await SeedNodeWithTemplate();

        var request = new HeartbeatRequest(
            NodeId: node.NodeId, NodeVersion: "1.0.0", UptimeSeconds: 100,
            DatabaseType: null, TransportMode: null,
            AppliedTemplateVersion: 1,
            AppliedEffectiveHash: "wrong-hash",
            ConfigurationApplyStatus: ConfigurationApplyStatus.Applied);

        var response = await _processor.ProcessAsync(node.NodeId, request, CancellationToken.None);

        response.ConfigurationState.Should().Be(ConfigurationState.Drifted);
    }

    [Fact]
    public async Task Process_FailedStatus_WritesApplyFailedHistoryEvent()
    {
        var (node, version) = await SeedNodeWithTemplate();

        var request = new HeartbeatRequest(
            NodeId: node.NodeId, NodeVersion: "1.0.0", UptimeSeconds: 100,
            DatabaseType: null, TransportMode: null,
            AppliedTemplateVersion: 1,
            AppliedEffectiveHash: "hash",
            ConfigurationApplyStatus: ConfigurationApplyStatus.Failed);

        await _processor.ProcessAsync(node.NodeId, request, CancellationToken.None);

        var failEvent = await _fx.Db.NodeConfigurationHistories
            .Where(h => h.NodeId == node.NodeId && h.EventType == "ApplyFailed")
            .FirstOrDefaultAsync();
        failEvent.Should().NotBeNull();
    }

    [Fact]
    public async Task Process_SameState_DoesNotDuplicateHistoryEvent()
    {
        var (node, version) = await SeedNodeWithTemplate();
        var hash = version.TemplateContentHash!;

        var request = new HeartbeatRequest(
            NodeId: node.NodeId, NodeVersion: null, UptimeSeconds: 0,
            DatabaseType: null, TransportMode: null,
            AppliedTemplateVersion: 1,
            AppliedEffectiveHash: hash,
            ConfigurationApplyStatus: ConfigurationApplyStatus.Applied);

        // Two identical heartbeats
        await _processor.ProcessAsync(node.NodeId, request, CancellationToken.None);
        await _processor.ProcessAsync(node.NodeId, request, CancellationToken.None);

        var historyCount = await _fx.Db.NodeConfigurationHistories
            .CountAsync(h => h.NodeId == node.NodeId && h.EventType == "Applied");

        historyCount.Should().Be(1, "consecutive same-state heartbeats must not create duplicate history");
    }
}
