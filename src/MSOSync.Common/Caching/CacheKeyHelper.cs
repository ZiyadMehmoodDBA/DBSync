namespace MSOSync.Common.Caching;

/// <summary>
/// Centralized cache key factory. Ensures consistent key format across all callers.
/// Pattern: {domain}:{entity}:{qualifier}
/// </summary>
public static class CacheKeyHelper
{
    // ── Metadata ──────────────────────────────────────────────────────────────

    /// <summary>Single node: "metadata:node:{nodeId}"</summary>
    public static string Node(string nodeId)
        => $"metadata:node:{nodeId}";

    /// <summary>Single channel: "metadata:channel:{channelId}"</summary>
    public static string Channel(string channelId)
        => $"metadata:channel:{channelId}";

    /// <summary>Single trigger: "metadata:trigger:{triggerId}"</summary>
    public static string Trigger(string triggerId)
        => $"metadata:trigger:{triggerId}";

    /// <summary>Single router: "metadata:router:{routerId}"</summary>
    public static string Router(string routerId)
        => $"metadata:router:{routerId}";

    /// <summary>Single parameter: "metadata:parameter:{name}"</summary>
    public static string Parameter(string name)
        => $"metadata:parameter:{name}";

    // ── Topology ──────────────────────────────────────────────────────────────

    /// <summary>Topology graph: "topology:graph"</summary>
    public static string TopologyGraph()
        => "topology:graph";

    /// <summary>Topology groups list: "topology:groups:v1"</summary>
    public static string TopologyGroups()
        => "topology:groups:v1";

    // ── Metrics ───────────────────────────────────────────────────────────────

    /// <summary>Metrics summary: "metrics:summary:v1"</summary>
    public static string MetricsSummary()
        => "metrics:summary:v1";

    /// <summary>Node metrics list: "metrics:nodes:v1"</summary>
    public static string MetricsNodes()
        => "metrics:nodes:v1";

    /// <summary>Channel metrics list: "metrics:channels:v1"</summary>
    public static string MetricsChannels()
        => "metrics:channels:v1";

    // ── Routing ───────────────────────────────────────────────────────────────

    /// <summary>Routing table for a trigger: "routing:trigger:{triggerId}"</summary>
    public static string RoutingTrigger(string triggerId)
        => $"routing:trigger:{triggerId}";

    // ── Permissions ───────────────────────────────────────────────────────────

    /// <summary>Role permission list: "permissions:{roleName}"</summary>
    public static string Permissions(string roleName)
        => $"permissions:{roleName}";

    // ── Overview ──────────────────────────────────────────────────────────────

    /// <summary>Overview snapshot: "overview:snapshot"</summary>
    public static string OverviewSnapshot()
        => "overview:snapshot";

    // ── Prefix helpers (for RemoveByPrefixAsync) ──────────────────────────────

    /// <summary>All metadata node keys: "metadata:node:"</summary>
    public static string NodePrefix() => "metadata:node:";

    /// <summary>All metadata channel keys: "metadata:channel:"</summary>
    public static string ChannelPrefix() => "metadata:channel:";

    /// <summary>All metadata trigger keys: "metadata:trigger:"</summary>
    public static string TriggerPrefix() => "metadata:trigger:";

    /// <summary>All routing keys: "routing:"</summary>
    public static string RoutingPrefix() => "routing:";

    /// <summary>All metrics keys: "metrics:"</summary>
    public static string MetricsPrefix() => "metrics:";

    /// <summary>All topology keys: "topology:"</summary>
    public static string TopologyPrefix() => "topology:";
}
