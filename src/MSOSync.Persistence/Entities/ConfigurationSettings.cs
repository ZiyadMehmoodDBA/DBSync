namespace MSOSync.Persistence.Entities;

public sealed record ConfigurationSettings
{
    public int HeartbeatIntervalSeconds { get; init; }
    public string TransportMode { get; init; } = "Push";          // Push / Pull / Both
    public int MaxRetryAttempts { get; init; }
    public int RetryBackoffSeconds { get; init; }
    public int BatchSizeLimit { get; init; }
    public string? MinimumAgentVersion { get; init; }             // semver e.g. "1.2.0"; null = no constraint
    public Dictionary<string, bool> FeatureFlags { get; init; } = [];
    public List<Guid> ChannelIds { get; init; } = [];
    public List<Guid> RouterIds { get; init; } = [];
    public List<Guid> TriggerIds { get; init; } = [];
}
