using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public sealed class EffectiveConfigurationComputer(
    AppDbContext db,
    IConfigurationValidationService validationSvc) : IEffectiveConfigurationComputer
{
    // validationSvc is reserved for future pre-compute validation gate (Task 4)
    private readonly IConfigurationValidationService _validationSvc = validationSvc;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<EffectiveConfigResult> ComputeAsync(
        SyncConfigurationTemplateVersion version, string nodeId, CancellationToken ct)
    {
        // 1. Parse template settings
        var settings = JsonSerializer.Deserialize<ConfigurationSettings>(version.SettingsJson, _json)!;

        // 2. Load node overrides and apply per-key replacement
        var overrides = await db.NodeConfigurationOverrides.AsNoTracking()
            .Where(o => o.NodeId == nodeId)
            .ToListAsync(ct);

        if (overrides.Count > 0)
            settings = ApplyOverrides(settings, overrides);

        // 3. Compute ExpectedEffectiveHash (includes override values)
        var effectiveHash = CanonicalJsonSerializer.ComputeHash(settings);

        return new EffectiveConfigResult(settings, effectiveHash);
    }

    private static ConfigurationSettings ApplyOverrides(
        ConfigurationSettings base_, IReadOnlyList<SyncNodeConfigurationOverride> overrides)
    {
        // Apply each override by key (camelCase setting key matches property)
        var result = base_;
        foreach (var o in overrides)
        {
            result = o.SettingKey switch
            {
                "heartbeatIntervalSeconds" when int.TryParse(o.SettingValue, out var v)
                    => result with { HeartbeatIntervalSeconds = v },
                "transportMode"
                    => result with { TransportMode = o.SettingValue },
                "maxRetryAttempts" when int.TryParse(o.SettingValue, out var v)
                    => result with { MaxRetryAttempts = v },
                "retryBackoffSeconds" when int.TryParse(o.SettingValue, out var v)
                    => result with { RetryBackoffSeconds = v },
                "batchSizeLimit" when int.TryParse(o.SettingValue, out var v)
                    => result with { BatchSizeLimit = v },
                "minimumAgentVersion"
                    => result with { MinimumAgentVersion = o.SettingValue },
                _ => result  // unknown key ignored (validated at assignment time)
            };
        }
        return result;
    }
}
