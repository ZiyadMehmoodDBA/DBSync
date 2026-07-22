using Microsoft.AspNetCore.Mvc;

namespace MSOSync.Api.Dtos.Audit;

public sealed record GetEntityHistoryRequest
{
    [FromQuery]
    public string?  Cursor   { get; init; }
    [FromQuery]
    public int      PageSize { get; init; } = 50;
}
