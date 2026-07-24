using MSOSync.Metadata.Dtos;

namespace MSOSync.Metadata.NodeManagement;

public sealed record NodeCursorFilter
{
    public string? Cursor        { get; init; }
    public int     PageSize      { get; init; } = 50;
    public bool    IncludeTotal  { get; init; } = false;

    public int ClampedPageSize => Math.Clamp(PageSize, 1, 200);
}

public sealed record NodeListGateResult(
    bool                    PaginationRequired,
    IReadOnlyList<NodeDto>? Items,
    string?                 NextCursor);
