using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Dtos.Nodes;

public sealed record NodeListGateResponse(
    bool                   PaginationRequired,
    IReadOnlyList<NodeDto> Items,
    string?                NextCursor,
    string                 CursorEndpoint);
