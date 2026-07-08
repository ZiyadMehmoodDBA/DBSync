using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

/// Pure drift state computation. No I/O. Called by HeartbeatProcessor only.
public sealed class DriftDetector : IDriftDetector
{
    public ConfigurationState Compute(SyncNode node, DateTime nowUtc,
        int heartbeatIntervalSeconds, int missedThreshold)
    {
        if (node.AssignedTemplateId is null)
            return ConfigurationState.None;

        // Stale threshold = HeartbeatIntervalSeconds * MissedThreshold * 2
        var staleThresholdSeconds = (double)(heartbeatIntervalSeconds * missedThreshold * 2);

        if (node.ConfigurationStatusReportedAt.HasValue)
        {
            var age = (nowUtc - node.ConfigurationStatusReportedAt.Value).TotalSeconds;
            if (age >= staleThresholdSeconds)
                return ConfigurationState.Unknown;
        }
        else
        {
            // Never reported — unknown
            return ConfigurationState.Unknown;
        }

        // ConfigurationApplyStatus takes precedence for Applying/Failed
        // (These are reported via heartbeat body; stored separately — read from context)
        // Note: Applying and Failed states are set by HeartbeatProcessor before calling Compute.
        // DriftDetector handles structural drift only.

        // Version check
        if (node.AssignedTemplateVersion != node.AppliedTemplateVersion)
            return ConfigurationState.UpdateAvailable;

        // Hash check — same version
        if (node.ExpectedEffectiveHash != node.AppliedEffectiveHash)
            return ConfigurationState.Drifted;

        return ConfigurationState.Current;
    }
}
