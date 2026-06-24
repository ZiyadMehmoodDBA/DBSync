namespace MSOSync.Metadata.Common;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int              Page,
    int              PageSize,
    int              TotalCount);
