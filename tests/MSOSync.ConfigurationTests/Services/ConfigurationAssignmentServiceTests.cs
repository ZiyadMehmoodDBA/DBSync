using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.ConfigurationTests;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Configuration;
using MSOSync.Persistence.Entities;
using Moq;
using System.Text.Json;
using Xunit;

namespace MSOSync.ConfigurationTests.Services;

public sealed class ConfigurationAssignmentServiceTests : IClassFixture<ConfigurationDbFixture>
{
    private readonly ConfigurationDbFixture _fx;
    private readonly IConfigurationAssignmentService _svc;
    private readonly Guid _userId = Guid.NewGuid();
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ConfigurationAssignmentServiceTests(ConfigurationDbFixture fx)
    {
        _fx  = fx;
        var validSvc = new ConfigurationValidationService(fx.Db);
        var computer = new EffectiveConfigurationComputer(fx.Db, validSvc);
        var auditSvc = Mock.Of<IAuditService>();
        _svc = new ConfigurationAssignmentService(fx.Db, computer, auditSvc);
    }

    private async Task<(SyncConfigurationTemplate template, SyncConfigurationTemplateVersion version, SyncNode node)>
        SeedPublishedTemplateAndNode(string nodeId = "assign-node")
    {
        var settings = new ConfigurationSettings
        {
            HeartbeatIntervalSeconds = 30, TransportMode = "Push",
            MaxRetryAttempts = 3, RetryBackoffSeconds = 60, BatchSizeLimit = 1000,
            FeatureFlags = [], ChannelIds = [], RouterIds = [], TriggerIds = [],
        };
        var template = new SyncConfigurationTemplate
        {
            Id = Guid.NewGuid(), Name = $"T-{nodeId}", Status = "Published",
            CurrentPublishedVersion = 1, CreatedBy = _userId,
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
            NodeId = nodeId, GroupId = "g", SyncUrl = "http://x",
        };
        _fx.Db.Nodes.Add(node);
        await _fx.Db.SaveChangesAsync();

        return (template, version, node);
    }

    [Fact]
    public async Task AssignAsync_SetsAssignmentAndExpectedHash()
    {
        var (template, version, node) = await SeedPublishedTemplateAndNode("assign-n1");

        var dto = await _svc.AssignAsync(node.NodeId, template.Id, 1, _userId, null, CancellationToken.None);

        dto.AssignedTemplateId.Should().Be(template.Id);
        dto.AssignedTemplateVersion.Should().Be(1);
        dto.ExpectedEffectiveHash.Should().NotBeNullOrEmpty().And.HaveLength(64);
        dto.ConfigurationState.Should().Be(ConfigurationState.UpdateAvailable);
    }

    [Fact]
    public async Task AssignAsync_WritesDomainHistoryEvent()
    {
        var (template, _, node) = await SeedPublishedTemplateAndNode("assign-history");
        await _svc.AssignAsync(node.NodeId, template.Id, 1, _userId, null, CancellationToken.None);

        var history = await _fx.Db.NodeConfigurationHistories
            .Where(h => h.NodeId == node.NodeId && h.EventType == "Assigned")
            .ToListAsync(default);
        history.Should().HaveCount(1);
    }

    [Fact]
    public async Task UnassignAsync_ClearsAssignmentAndSetsNone()
    {
        var (template, _, node) = await SeedPublishedTemplateAndNode("unassign-n1");
        await _svc.AssignAsync(node.NodeId, template.Id, 1, _userId, null, CancellationToken.None);
        await _svc.UnassignAsync(node.NodeId, _userId, CancellationToken.None);

        var updated = await _fx.Db.Nodes.FindAsync(node.NodeId);
        updated!.AssignedTemplateId.Should().BeNull();
        updated.ConfigurationState.Should().Be(ConfigurationState.None);
    }

    [Fact]
    public async Task SetOverrideAsync_RecomputesExpectedHash()
    {
        var (template, _, node) = await SeedPublishedTemplateAndNode("override-n1");
        var dto1 = await _svc.AssignAsync(node.NodeId, template.Id, 1, _userId, null, CancellationToken.None);

        await _svc.SetOverrideAsync(node.NodeId, "batchSizeLimit", "500", "Manual", _userId, CancellationToken.None);

        var dto2 = await _svc.GetNodeConfigurationAsync(node.NodeId, CancellationToken.None);
        dto2.ExpectedEffectiveHash.Should().NotBe(dto1.ExpectedEffectiveHash);
    }

    [Fact]
    public async Task RemoveOverrideAsync_RestoresOriginalHash()
    {
        var (template, _, node) = await SeedPublishedTemplateAndNode("remove-override-n1");
        var dto1 = await _svc.AssignAsync(node.NodeId, template.Id, 1, _userId, null, CancellationToken.None);
        var originalHash = dto1.ExpectedEffectiveHash;

        await _svc.SetOverrideAsync(node.NodeId, "batchSizeLimit", "500", "Manual", _userId, CancellationToken.None);
        await _svc.RemoveOverrideAsync(node.NodeId, "batchSizeLimit", _userId, CancellationToken.None);

        var dto2 = await _svc.GetNodeConfigurationAsync(node.NodeId, CancellationToken.None);
        dto2.ExpectedEffectiveHash.Should().Be(originalHash);
    }

    [Fact]
    public async Task AssignAsync_Reassign_WritesUnassignedHistoryForOldTemplate()
    {
        // Seed two templates and one node
        var nodeId   = "reassign-node-1";
        var settings = new ConfigurationSettings
        {
            HeartbeatIntervalSeconds = 30, TransportMode = "Push",
            MaxRetryAttempts = 3, RetryBackoffSeconds = 60, BatchSizeLimit = 1000,
            FeatureFlags = [], ChannelIds = [], RouterIds = [], TriggerIds = [],
        };

        var templateA = new SyncConfigurationTemplate
        {
            Id = Guid.NewGuid(), Name = $"TA-{nodeId}", Status = "Published",
            CurrentPublishedVersion = 1, CreatedBy = _userId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _fx.Db.ConfigurationTemplates.Add(templateA);

        var hash = CanonicalJsonSerializer.ComputeHash(settings);
        var versionA = new SyncConfigurationTemplateVersion
        {
            Id = Guid.NewGuid(), TemplateId = templateA.Id, VersionNumber = 1,
            IsDraft = false, SettingsJson = JsonSerializer.Serialize(settings, _json),
            TemplateContentHash = hash, SchemaVersion = 1,
        };
        _fx.Db.ConfigurationTemplateVersions.Add(versionA);

        var templateB = new SyncConfigurationTemplate
        {
            Id = Guid.NewGuid(), Name = $"TB-{nodeId}", Status = "Published",
            CurrentPublishedVersion = 1, CreatedBy = _userId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _fx.Db.ConfigurationTemplates.Add(templateB);

        var versionB = new SyncConfigurationTemplateVersion
        {
            Id = Guid.NewGuid(), TemplateId = templateB.Id, VersionNumber = 1,
            IsDraft = false, SettingsJson = JsonSerializer.Serialize(settings, _json),
            TemplateContentHash = hash, SchemaVersion = 1,
        };
        _fx.Db.ConfigurationTemplateVersions.Add(versionB);

        var node = new SyncNode { NodeId = nodeId, GroupId = "g", SyncUrl = "http://x" };
        _fx.Db.Nodes.Add(node);
        await _fx.Db.SaveChangesAsync();

        // Assign template A
        await _svc.AssignAsync(nodeId, templateA.Id, 1, _userId, null, CancellationToken.None);

        // Reassign to template B
        await _svc.AssignAsync(nodeId, templateB.Id, 1, _userId, null, CancellationToken.None);

        var history = await _fx.Db.NodeConfigurationHistories
            .Where(h => h.NodeId == nodeId)
            .OrderBy(h => h.OccurredAt)
            .ToListAsync(default);

        // Should have: Assigned(A), Unassigned(A), Assigned(B)
        history.Should().HaveCount(3);
        history[0].EventType.Should().Be("Assigned");
        history[0].TemplateId.Should().Be(templateA.Id);
        history[1].EventType.Should().Be(ConfigurationAuditConstants.Unassigned);
        history[1].TemplateId.Should().Be(templateA.Id);
        history[2].EventType.Should().Be("Assigned");
        history[2].TemplateId.Should().Be(templateB.Id);
    }
}
