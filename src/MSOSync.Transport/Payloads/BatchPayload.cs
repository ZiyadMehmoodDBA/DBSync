namespace MSOSync.Transport.Payloads;

// Entire HTTP body is gzip-compressed; Events is the uncompressed list
public sealed record BatchPayload(
    long                        BatchId,
    long                        BatchSequence,
    string                      ChannelId,
    string                      SourceNodeId,
    string                      TargetNodeId,
    int                         RowCount,
    IReadOnlyList<EventPayload> Events);
