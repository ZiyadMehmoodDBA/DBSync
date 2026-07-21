namespace MSOSync.Metadata.OutgoingBatches;

public sealed record OutgoingBatchQueryFilter(
    string? NodeId,
    string? ChannelId,
    byte?   Status,
    string  SortBy,
    string  SortDirection,
    int     Page,
    int     PageSize);

public sealed record OutgoingBatchRow(
    long     BatchId,
    byte     Status,
    string   NodeId,
    string   ChannelId,
    DateTime? CreateTime,
    DateTime? SentTime,
    DateTime? AckTime,
    int      RetryCount,
    int      RowCount,
    string?  LatestError);

public sealed record OutgoingBatchPage(IReadOnlyList<OutgoingBatchRow> Items, int Total);
