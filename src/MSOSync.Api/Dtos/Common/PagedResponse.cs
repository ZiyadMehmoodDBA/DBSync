namespace MSOSync.Api.Dtos.Common;

public sealed record PagedResponse<T>(
    IEnumerable<T> Data,
    int Total,
    int Page,
    int PageSize,
    int TotalPages);
