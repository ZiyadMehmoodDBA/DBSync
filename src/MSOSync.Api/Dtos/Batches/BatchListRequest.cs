using Microsoft.AspNetCore.Mvc;

namespace MSOSync.Api.Dtos.Batches;

public sealed record BatchListRequest
{
    [FromQuery] public string? Status        { get; init; }
    [FromQuery] public string? NodeId        { get; init; }
    [FromQuery] public string? ChannelId     { get; init; }
    [FromQuery] public int     Page          { get; init; } = 1;
    [FromQuery] public int     PageSize      { get; init; } = 20;
    [FromQuery] public string  SortBy        { get; init; } = "createTime";
    [FromQuery] public string  SortDirection { get; init; } = "desc";
}
