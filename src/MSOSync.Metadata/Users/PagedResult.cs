namespace MSOSync.Metadata.Users;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int              Page,
    int              PageSize,
    int              TotalCount);
