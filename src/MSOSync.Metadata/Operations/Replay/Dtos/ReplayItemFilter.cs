namespace MSOSync.Metadata.Operations.Replay.Dtos;

public sealed record ReplayItemFilter(
    string? Status,
    string? Cursor,
    int     PageSize = 50);
