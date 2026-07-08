using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public sealed class ConfigurationValidationService(AppDbContext db) : IConfigurationValidationService
{
    private static readonly IReadOnlySet<string> ValidTransportModes =
        new HashSet<string>(StringComparer.Ordinal) { "Push", "Pull", "Both" };

    public async Task<ValidationResult> ValidateAsync(ConfigurationSettings s, CancellationToken ct)
    {
        var errors = new List<ValidationError>();

        // Rule 1: referenced Channel entities must exist
        if (s.ChannelIds.Count > 0)
        {
            var channelIdStrings = s.ChannelIds.Select(g => g.ToString()).ToList();
            var existing = await db.Channels.AsNoTracking()
                .Where(c => channelIdStrings.Contains(c.ChannelId))
                .Select(c => c.ChannelId).ToListAsync(ct);
            var missing = channelIdStrings.Except(existing, StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
                errors.Add(new("ChannelIds", $"Channel IDs not found: {string.Join(", ", missing)}"));
        }

        // Rule 2: referenced Router entities must exist
        if (s.RouterIds.Count > 0)
        {
            var routerIdStrings = s.RouterIds.Select(g => g.ToString()).ToList();
            var existing = await db.Routers.AsNoTracking()
                .Where(r => routerIdStrings.Contains(r.RouterId))
                .Select(r => r.RouterId).ToListAsync(ct);
            var missing = routerIdStrings.Except(existing, StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
                errors.Add(new("RouterIds", $"Router IDs not found: {string.Join(", ", missing)}"));
        }

        // Rule 3: referenced Trigger entities must exist
        if (s.TriggerIds.Count > 0)
        {
            var triggerIdStrings = s.TriggerIds.Select(g => g.ToString()).ToList();
            var existing = await db.Triggers.AsNoTracking()
                .Where(t => triggerIdStrings.Contains(t.TriggerId))
                .Select(t => t.TriggerId).ToListAsync(ct);
            var missing = triggerIdStrings.Except(existing, StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
                errors.Add(new("TriggerIds", $"Trigger IDs not found: {string.Join(", ", missing)}"));
        }

        // Rule 5: no duplicate IDs
        if (s.ChannelIds.Distinct().Count() != s.ChannelIds.Count)
            errors.Add(new("ChannelIds", "Duplicate channel IDs"));
        if (s.RouterIds.Distinct().Count() != s.RouterIds.Count)
            errors.Add(new("RouterIds", "Duplicate router IDs"));
        if (s.TriggerIds.Distinct().Count() != s.TriggerIds.Count)
            errors.Add(new("TriggerIds", "Duplicate trigger IDs"));

        // Rule 6: HeartbeatIntervalSeconds 1–3600
        if (s.HeartbeatIntervalSeconds <= 0 || s.HeartbeatIntervalSeconds > 3600)
            errors.Add(new("HeartbeatIntervalSeconds", "Must be between 1 and 3600"));

        // Rule 7: MaxRetryAttempts 0–20
        if (s.MaxRetryAttempts < 0 || s.MaxRetryAttempts > 20)
            errors.Add(new("MaxRetryAttempts", "Must be between 0 and 20"));

        // Rule 8: BatchSizeLimit 1–10000
        if (s.BatchSizeLimit <= 0 || s.BatchSizeLimit > 10_000)
            errors.Add(new("BatchSizeLimit", "Must be between 1 and 10000"));

        // Rule 9: no duplicate feature flag keys (Dictionary already enforces uniqueness; defensive check)
        if (s.FeatureFlags.Count != s.FeatureFlags.Keys.Distinct(StringComparer.Ordinal).Count())
            errors.Add(new("FeatureFlags", "Duplicate feature flag keys"));

        // Rule 10: all feature flag keys must be in catalog
        var unknownFlags = s.FeatureFlags.Keys
            .Where(k => !FeatureFlagCatalog.IsSupportedKey(k)).ToList();
        if (unknownFlags.Count > 0)
            errors.Add(new("FeatureFlags", $"Unknown feature flag keys: {string.Join(", ", unknownFlags)}"));

        // Rule 11: TransportMode must be a valid value
        if (!ValidTransportModes.Contains(s.TransportMode))
            errors.Add(new("TransportMode", "Must be one of: Push, Pull, Both"));

        return errors.Count == 0 ? ValidationResult.Ok : ValidationResult.Fail(errors);
    }
}
