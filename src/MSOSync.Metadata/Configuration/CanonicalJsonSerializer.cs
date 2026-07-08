using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

/// <summary>
/// Canonical JSON serialization contract. Changing this breaks drift detection across all nodes.
/// Contract: sorted property names, sorted order-invariant collections, no whitespace, UTF-8, SHA-256.
/// </summary>
public static class CanonicalJsonSerializer
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = false,
    };

    public static string ComputeHash(ConfigurationSettings settings)
    {
        var canonical = ToCanonical(settings);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ToCanonical(ConfigurationSettings settings)
    {
        // Build an ordered representation — property names sorted lexicographically.
        // Collections that are order-invariant (Ids, FeatureFlags) are sorted before serialization.
        var sortedChannelIds = settings.ChannelIds.OrderBy(g => g.ToString()).ToList();
        var sortedRouterIds  = settings.RouterIds.OrderBy(g => g.ToString()).ToList();
        var sortedTriggerIds = settings.TriggerIds.OrderBy(g => g.ToString()).ToList();
        var sortedFlags = settings.FeatureFlags
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Serialize using a SortedDictionary to guarantee property order.
        var doc = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["batchSizeLimit"]           = settings.BatchSizeLimit,
            ["channelIds"]               = sortedChannelIds,
            ["featureFlags"]             = sortedFlags,
            ["heartbeatIntervalSeconds"] = settings.HeartbeatIntervalSeconds,
            ["maxRetryAttempts"]         = settings.MaxRetryAttempts,
            ["minimumAgentVersion"]      = (object?)settings.MinimumAgentVersion,
            ["retryBackoffSeconds"]      = settings.RetryBackoffSeconds,
            ["routerIds"]                = sortedRouterIds,
            ["transportMode"]            = settings.TransportMode,
            ["triggerIds"]               = sortedTriggerIds,
        };

        return JsonSerializer.Serialize(doc, _opts);
    }
}
