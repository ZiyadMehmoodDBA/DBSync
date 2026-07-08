using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public sealed class ConfigurationAssignmentService(
    AppDbContext db,
    IEffectiveConfigurationComputer computer,
    IAuditService auditSvc) : IConfigurationAssignmentService
{
    public async Task<NodeConfigurationDto> AssignAsync(string nodeId, Guid templateId, int version,
        Guid userId, string? correlationId, CancellationToken ct)
    {
        var node = await db.Nodes.FindAsync([nodeId], ct)
            ?? throw new NotFoundException($"Node {nodeId} not found");

        // Pre-flight: template must be Published
        var template = await db.ConfigurationTemplates.FindAsync([templateId], ct)
            ?? throw new NotFoundException($"Template {templateId} not found");
        if (template.Status != "Published")
            throw new InvalidOperationException("Template must be Published before assignment");

        var templateVersion = await db.ConfigurationTemplateVersions
            .FirstOrDefaultAsync(v => v.TemplateId == templateId
                && v.VersionNumber == version && !v.IsDraft, ct)
            ?? throw new NotFoundException($"Template {templateId} version {version} not found");

        // Pre-flight: node lifecycle
        if (node.LifecycleState is NodeLifecycleState.Decommissioned
            or NodeLifecycleState.Decommissioning)
            throw new InvalidOperationException("Cannot assign template to a decommissioned node");

        // Compute ExpectedEffectiveHash
        var effective = await computer.ComputeAsync(templateVersion, nodeId, ct);

        node.AssignedTemplateId      = templateId;
        node.AssignedTemplateVersion = version;
        node.ExpectedEffectiveHash   = effective.EffectiveHash;
        node.ConfigurationState      = ConfigurationState.UpdateAvailable;

        // Write history event
        db.NodeConfigurationHistories.Add(new SyncNodeConfigurationHistory
        {
            Id                = Guid.NewGuid(),
            NodeId            = nodeId,
            EventType         = "Assigned",
            TemplateId        = templateId,
            TemplateVersion   = version,
            ConfigurationHash = effective.EffectiveHash,
            CorrelationId     = correlationId,
            ActorId           = userId,
            OccurredAt        = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        await auditSvc.WriteAsync(ConfigurationAuditConstants.Assigned,
            $"Template '{template.Name}' v{version} assigned to node {nodeId}",
            userId.ToString(), ct);

        return await GetNodeConfigurationAsync(nodeId, ct);
    }

    public async Task UnassignAsync(string nodeId, Guid userId, CancellationToken ct)
    {
        var node = await db.Nodes.FindAsync([nodeId], ct)
            ?? throw new NotFoundException($"Node {nodeId} not found");

        var previousTemplateId = node.AssignedTemplateId;

        node.AssignedTemplateId      = null;
        node.AssignedTemplateVersion = null;
        node.ExpectedEffectiveHash   = null;
        node.ConfigurationState      = ConfigurationState.None;

        if (previousTemplateId.HasValue)
        {
            db.NodeConfigurationHistories.Add(new SyncNodeConfigurationHistory
            {
                Id         = Guid.NewGuid(),
                NodeId     = nodeId,
                EventType  = "Unassigned",
                TemplateId = previousTemplateId,
                ActorId    = userId,
                OccurredAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);

        await auditSvc.WriteAsync(ConfigurationAuditConstants.Unassigned,
            $"Template unassigned from node {nodeId}", userId.ToString(), ct);
    }

    public async Task<NodeConfigurationDto> GetNodeConfigurationAsync(string nodeId, CancellationToken ct)
    {
        var node = await db.Nodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct)
            ?? throw new NotFoundException($"Node {nodeId} not found");

        var overrides = await db.NodeConfigurationOverrides.AsNoTracking()
            .Where(o => o.NodeId == nodeId)
            .Select(o => new NodeOverrideDto(o.Id, o.SettingKey, o.SettingValue, o.OverrideSource, o.UpdatedAt))
            .ToListAsync(ct);

        ConfigurationSettings? effectiveSettings = null;
        if (node.AssignedTemplateId.HasValue && node.AssignedTemplateVersion.HasValue)
        {
            var ver = await db.ConfigurationTemplateVersions.AsNoTracking()
                .FirstOrDefaultAsync(v =>
                    v.TemplateId    == node.AssignedTemplateId &&
                    v.VersionNumber == node.AssignedTemplateVersion &&
                    !v.IsDraft, ct);
            if (ver is not null)
            {
                var result = await computer.ComputeAsync(ver, nodeId, ct);
                effectiveSettings = result.Settings;
            }
        }

        return new NodeConfigurationDto(
            node.NodeId,
            node.AssignedTemplateId,
            node.AssignedTemplateVersion,
            node.AppliedTemplateVersion,
            node.ExpectedEffectiveHash,
            node.AppliedEffectiveHash,
            node.ConfigurationState,
            node.LastAppliedAt,
            effectiveSettings,
            overrides);
    }

    public async Task<IReadOnlyList<ConfigurationHistoryEventDto>> GetNodeHistoryAsync(
        string nodeId, CancellationToken ct)
    {
        return await db.NodeConfigurationHistories.AsNoTracking()
            .Where(h => h.NodeId == nodeId)
            .OrderByDescending(h => h.OccurredAt)
            .Take(200)
            .Select(h => new ConfigurationHistoryEventDto(
                h.Id, h.NodeId, h.EventType, h.TemplateId, h.TemplateVersion,
                h.ConfigurationHash, h.CorrelationId, h.ActorId, h.OccurredAt, h.Notes))
            .ToListAsync(ct);
    }

    public async Task SetOverrideAsync(string nodeId, string key, string value, string source,
        Guid userId, CancellationToken ct)
    {
        var existing = await db.NodeConfigurationOverrides
            .FirstOrDefaultAsync(o => o.NodeId == nodeId && o.SettingKey == key, ct);

        if (existing is null)
        {
            db.NodeConfigurationOverrides.Add(new SyncNodeConfigurationOverride
            {
                Id             = Guid.NewGuid(),
                NodeId         = nodeId,
                SettingKey     = key,
                SettingValue   = value,
                OverrideSource = source,
                UpdatedBy      = userId,
                UpdatedAt      = DateTime.UtcNow,
            });
        }
        else
        {
            existing.SettingValue   = value;
            existing.OverrideSource = source;
            existing.UpdatedBy      = userId;
            existing.UpdatedAt      = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        // Recompute ExpectedEffectiveHash
        await RecomputeExpectedHashAsync(nodeId, ct);

        await auditSvc.WriteAsync(ConfigurationAuditConstants.OverrideSet,
            $"Override '{key}' set on node {nodeId}", userId.ToString(), ct);
    }

    public async Task RemoveOverrideAsync(string nodeId, string key, Guid userId, CancellationToken ct)
    {
        var existing = await db.NodeConfigurationOverrides
            .FirstOrDefaultAsync(o => o.NodeId == nodeId && o.SettingKey == key, ct);

        if (existing is not null)
        {
            db.NodeConfigurationOverrides.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        await RecomputeExpectedHashAsync(nodeId, ct);

        await auditSvc.WriteAsync(ConfigurationAuditConstants.OverrideRemoved,
            $"Override '{key}' removed from node {nodeId}", userId.ToString(), ct);
    }

    public async Task<DriftSummaryDto> GetDriftSummaryAsync(CancellationToken ct)
    {
        var counts = await db.Nodes.AsNoTracking()
            .GroupBy(n => n.ConfigurationState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Get(ConfigurationState? s) =>
            counts.FirstOrDefault(c => c.State == s)?.Count ?? 0;

        return new DriftSummaryDto(
            Get(null),
            Get(ConfigurationState.Current),
            Get(ConfigurationState.UpdateAvailable),
            Get(ConfigurationState.Applying),
            Get(ConfigurationState.Drifted),
            Get(ConfigurationState.Failed),
            Get(ConfigurationState.Unknown));
    }

    public async Task<IReadOnlyList<DriftNodeDto>> GetDriftNodesAsync(
        string? stateFilter, Guid? templateId, int? version,
        string? nodeGroup, string? search, CancellationToken ct)
    {
        var query = db.Nodes.AsNoTracking();

        if (!string.IsNullOrEmpty(stateFilter) &&
            Enum.TryParse<ConfigurationState>(stateFilter, out var parsedState))
            query = query.Where(n => n.ConfigurationState == parsedState);

        if (templateId.HasValue)
            query = query.Where(n => n.AssignedTemplateId == templateId);

        if (version.HasValue)
            query = query.Where(n => n.AssignedTemplateVersion == version);

        if (!string.IsNullOrEmpty(nodeGroup))
            query = query.Where(n => n.GroupId == nodeGroup);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(n => n.NodeName.StartsWith(search) || n.NodeId.StartsWith(search));

        return await query
            .OrderBy(n => n.NodeId)
            .Select(n => new DriftNodeDto(
                n.NodeId, n.NodeName, n.GroupId,
                n.AssignedTemplateId, null,
                n.AssignedTemplateVersion, n.AppliedTemplateVersion,
                n.ExpectedEffectiveHash, n.AppliedEffectiveHash,
                n.ConfigurationState, n.ConfigurationStatusReportedAt))
            .ToListAsync(ct);
    }

    private async Task RecomputeExpectedHashAsync(string nodeId, CancellationToken ct)
    {
        var node = await db.Nodes.FindAsync([nodeId], ct);
        if (node?.AssignedTemplateId is null || node.AssignedTemplateVersion is null) return;

        var ver = await db.ConfigurationTemplateVersions
            .FirstOrDefaultAsync(v =>
                v.TemplateId    == node.AssignedTemplateId &&
                v.VersionNumber == node.AssignedTemplateVersion &&
                !v.IsDraft, ct);
        if (ver is null) return;

        var effective = await computer.ComputeAsync(ver, nodeId, ct);
        node.ExpectedEffectiveHash = effective.EffectiveHash;
        await db.SaveChangesAsync(ct);
    }
}
