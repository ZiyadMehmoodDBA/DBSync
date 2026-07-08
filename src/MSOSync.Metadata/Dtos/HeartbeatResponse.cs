using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Dtos;

public sealed record HeartbeatResponse(
    Guid?                AssignedTemplateId,
    int?                 AssignedTemplateVersion,
    string?              ContentHash,            // ExpectedEffectiveHash
    ConfigurationState?  ConfigurationState);
