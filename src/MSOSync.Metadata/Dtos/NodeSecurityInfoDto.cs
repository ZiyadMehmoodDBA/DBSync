namespace MSOSync.Metadata.Dtos;

public sealed record NodeSecurityInfoDto(
    string NodeId,
    bool HasPendingRotation,
    DateTime? RotationScheduled,
    DateTime? CreatedTime);
