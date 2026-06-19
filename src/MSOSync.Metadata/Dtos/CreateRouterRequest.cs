namespace MSOSync.Metadata.Dtos;

public sealed record CreateRouterRequest(
    string RouterId,
    string SourceNodeGroup,
    string TargetNodeGroup,
    string RouterType = "default");
