using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public sealed class ConfigurationValidationService(AppDbContext db) : IConfigurationValidationService
{
    private static readonly IReadOnlySet<string> ValidTransportModes =
        new HashSet<string>(StringComparer.Ordinal) { "Push", "Pull", "Both" };

    private const int MaxSupportedSchemaVersion = 1;

    public async Task<ValidationResult> ValidateAsync(ConfigurationSettings s, CancellationToken ct,
        int schemaVersion = 1)
    {
        var errors = new List<ValidationError>();

        // Rule 12: SchemaVersion must not exceed maximum supported version
        if (schemaVersion > MaxSupportedSchemaVersion)
            errors.Add(new("SchemaVersion",
                $"Schema version {schemaVersion} is not supported; maximum supported is {MaxSupportedSchemaVersion}"));

        // Rule 1: referenced Channel entities must exist and be enabled
        if (s.ChannelIds.Count > 0)
        {
            var channelIdStrings = s.ChannelIds.Select(g => g.ToString()).ToList();
            var found = await db.Channels.AsNoTracking()
                .Where(c => channelIdStrings.Contains(c.ChannelId))
                .Select(c => new { c.ChannelId, c.Enabled })
                .ToListAsync(ct);
            var foundIds = found.Select(c => c.ChannelId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = channelIdStrings.Where(id => !foundIds.Contains(id)).ToList();
            if (missing.Count > 0)
                errors.Add(new("ChannelIds", $"Channel IDs not found: {string.Join(", ", missing)}"));
            foreach (var c in found.Where(c => !c.Enabled))
                errors.Add(new("ChannelIds", $"Channel '{c.ChannelId}' is disabled"));
        }

        // Rule 2: referenced Router entities must exist and be enabled
        if (s.RouterIds.Count > 0)
        {
            var routerIdStrings = s.RouterIds.Select(g => g.ToString()).ToList();
            var found = await db.Routers.AsNoTracking()
                .Where(r => routerIdStrings.Contains(r.RouterId))
                .Select(r => new { r.RouterId, r.Enabled })
                .ToListAsync(ct);
            var foundIds = found.Select(r => r.RouterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = routerIdStrings.Where(id => !foundIds.Contains(id)).ToList();
            if (missing.Count > 0)
                errors.Add(new("RouterIds", $"Router IDs not found: {string.Join(", ", missing)}"));
            foreach (var r in found.Where(r => !r.Enabled))
                errors.Add(new("RouterIds", $"Router '{r.RouterId}' is disabled"));
        }

        // Rule 3: referenced Trigger entities must exist and be enabled
        if (s.TriggerIds.Count > 0)
        {
            var triggerIdStrings = s.TriggerIds.Select(g => g.ToString()).ToList();
            var found = await db.Triggers.AsNoTracking()
                .Where(t => triggerIdStrings.Contains(t.TriggerId))
                .Select(t => new { t.TriggerId, t.Enabled })
                .ToListAsync(ct);
            var foundIds = found.Select(t => t.TriggerId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = triggerIdStrings.Where(id => !foundIds.Contains(id)).ToList();
            if (missing.Count > 0)
                errors.Add(new("TriggerIds", $"Trigger IDs not found: {string.Join(", ", missing)}"));
            foreach (var t in found.Where(t => !t.Enabled))
                errors.Add(new("TriggerIds", $"Trigger '{t.TriggerId}' is disabled"));
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
