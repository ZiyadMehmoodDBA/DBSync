namespace MSOSync.Metadata.Dtos;

public sealed record UpdateRouterRequest(
    string SourceNodeGroup,
    string TargetNodeGroup,
    string RouterType);
