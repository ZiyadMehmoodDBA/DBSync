namespace MSOSync.Api.Dtos.Metadata;

public sealed record MetadataSummaryResponse(
    int Nodes,
    int Triggers,
    int Routers,
    int Channels,
    int Parameters);
