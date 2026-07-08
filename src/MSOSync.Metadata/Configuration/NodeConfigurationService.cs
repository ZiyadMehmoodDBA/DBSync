using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Configuration;

public sealed class NodeConfigurationService(
    AppDbContext db,
    IEffectiveConfigurationComputer computer) : INodeConfigurationService
{
    public async Task<CurrentConfigResult> GetCurrentAsync(string nodeId, string? ifNoneMatch, CancellationToken ct)
    {
        var node = await db.Nodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);

        if (node is null || node.AssignedTemplateId is null || node.AssignedTemplateVersion is null)
            return new CurrentConfigResult(false, null, null);

        var version = await db.ConfigurationTemplateVersions.AsNoTracking()
            .FirstOrDefaultAsync(v =>
                v.TemplateId    == node.AssignedTemplateId &&
                v.VersionNumber == node.AssignedTemplateVersion &&
                !v.IsDraft, ct);

        if (version is null)
            return new CurrentConfigResult(false, null, null);

        // ETag = ExpectedEffectiveHash
        var eTag = node.ExpectedEffectiveHash;

        // ETag match → 304
        if (!string.IsNullOrEmpty(ifNoneMatch) &&
            ifNoneMatch.Trim('"') == eTag)
            return new CurrentConfigResult(true, null, eTag);

        var effective = await computer.ComputeAsync(version, nodeId, ct);

        var dto = new CurrentConfigDto(
            TemplateId:           node.AssignedTemplateId.Value,
            TemplateVersion:      node.AssignedTemplateVersion.Value,
            ContentHash:          version.TemplateContentHash ?? effective.EffectiveHash,
            ConfigurationVersion: node.AssignedTemplateVersion.Value,
            SchemaVersion:        version.SchemaVersion,
            Effective:            effective.Settings);

        return new CurrentConfigResult(false, dto, eTag);
    }
}
