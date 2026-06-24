using MSOSync.Persistence;

namespace MSOSync.Metadata.IncomingBatches;

public sealed record IncomingBatchDetailDto(
    long                 BatchId,
    string               SourceNodeId,
    string               ChannelId,
    IncomingBatchStatus  Status,
    int?                 RowCount,
    long                 BatchSequence,
    DateTime             ReceivedTime,
    DateTime?            LoadTime,
    DateTime?            ExtractTime,
    DateTime?            AppliedTime,
    long?                ApplyTimeMs);
