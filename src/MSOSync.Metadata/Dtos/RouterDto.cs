namespace MSOSync.Metadata.Dtos;

public sealed record RouterDto(
    string RouterId,
    string SourceNodeGroup,
    string TargetNodeGroup,
    string RouterType,
    bool Enabled);
