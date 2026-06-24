using MSOSync.Persistence;

namespace MSOSync.Metadata.IncomingBatches;

public sealed record IncomingBatchSummaryDto(
    long                 BatchId,
    string               SourceNodeId,
    string               ChannelId,
    IncomingBatchStatus  Status,
    int?                 RowCount,
    long                 BatchSequence,
    DateTime             ReceivedTime,
    long?                ApplyTimeMs);
