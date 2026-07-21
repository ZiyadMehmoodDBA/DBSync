using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MSOSync.Metadata.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

/// <summary>
/// Sole writer of SyncNode configuration state columns on heartbeat.
/// Called by NodesController.Heartbeat.
/// </summary>
public sealed class HeartbeatProcessor(
    AppDbContext db,
    IDriftDetector detector,
    IConfiguration? configuration = null)
{

    private int HeartbeatIntervalSeconds => int.TryParse(
        configuration?["Heartbeat:IntervalSeconds"], out var v) ? v : 30;
    private int MissedThreshold => int.TryParse(
        configuration?["Heartbeat:MissedThreshold"], out var v) ? v : 3;

    public async Task<HeartbeatResponse> ProcessAsync(
        string nodeId, HeartbeatRequest request, CancellationToken ct)
    {
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
        if (node is null)
            return new HeartbeatResponse(null, null, null, null);

        var now = DateTime.UtcNow;
        var previousState = node.ConfigurationState;

        // Write reported values — ONLY this method and ConfigurationAssignmentService may write these columns
        if (!string.IsNullOrWhiteSpace(request.NodeVersion) && node.AgentVersion != request.NodeVersion)
            node.AgentVersion = request.NodeVersion;
        if (request.AppliedTemplateVersion.HasValue)
            node.AppliedTemplateVersion = request.AppliedTemplateVersion.Value;
        if (request.AppliedEffectiveHash is not null)
            node.AppliedEffectiveHash = request.AppliedEffectiveHash;
        node.ConfigurationStatusReportedAt = now;

        // Handle Applying / Failed before structural drift check
        if (request.ConfigurationApplyStatus == ConfigurationApplyStatus.Applying)
        {
            node.ConfigurationState = ConfigurationState.Applying;
        }
        else if (request.ConfigurationApplyStatus == ConfigurationApplyStatus.Failed)
        {
            node.ConfigurationState = ConfigurationState.Failed;

            // Write ApplyFailed history event (always — not deduplicated)
            db.NodeConfigurationHistories.Add(new SyncNodeConfigurationHistory
            {
                Id               = Guid.NewGuid(),
                NodeId           = nodeId,
                EventType        = "ApplyFailed",
                TemplateId       = node.AssignedTemplateId,
                TemplateVersion  = node.AppliedTemplateVersion,
                ConfigurationHash = node.AppliedEffectiveHash,
                OccurredAt       = now,
            });
        }
        else
        {
            // ConfigurationApplyStatus.Applied: fall through to DriftDetector.Compute.
            // DriftDetector returns Current when hashes match, Drifted when they diverge —
            // which is the correct semantic for an "Applied" report. No separate branch needed.
            node.ConfigurationState = detector.Compute(node, now,
                HeartbeatIntervalSeconds, MissedThreshold);
        }

        // Dedup: only write history event if state changed
        if (node.ConfigurationState != previousState)
        {
            var eventType = node.ConfigurationState switch
            {
                ConfigurationState.Current         => "Applied",
                ConfigurationState.Drifted         => "DriftDetected",
                ConfigurationState.UpdateAvailable => null, // not tracked
                _                                  => null,
            };

            if (eventType is "Applied" && previousState == ConfigurationState.Drifted)
                eventType = "DriftCleared";

            if (eventType is not null)
            {
                db.NodeConfigurationHistories.Add(new SyncNodeConfigurationHistory
                {
                    Id               = Guid.NewGuid(),
                    NodeId           = nodeId,
                    EventType        = eventType,
                    TemplateId       = node.AssignedTemplateId,
                    TemplateVersion  = node.AppliedTemplateVersion,
                    ConfigurationHash = node.AppliedEffectiveHash,
                    OccurredAt       = now,
                });
            }

            if (node.ConfigurationState == ConfigurationState.Current)
                node.LastAppliedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return new HeartbeatResponse(
            node.AssignedTemplateId,
            node.AssignedTemplateVersion,
            node.ExpectedEffectiveHash,
            node.ConfigurationState);
    }
}
