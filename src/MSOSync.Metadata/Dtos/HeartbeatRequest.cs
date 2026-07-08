using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Dtos;

public sealed record HeartbeatRequest(
    string  NodeId,
    string? NodeVersion,
    long    UptimeSeconds,
    string? DatabaseType,
    string? TransportMode,
    // Epic 12B-2 additions — all nullable (missing = None/not reporting)
    int?    AppliedTemplateVersion        = null,
    string? AppliedEffectiveHash          = null,
    ConfigurationApplyStatus? ConfigurationApplyStatus = null);
