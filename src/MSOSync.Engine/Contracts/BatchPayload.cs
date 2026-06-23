namespace MSOSync.Engine;

public sealed record BatchPayload(
    long                        BatchId,
    long                        BatchSequence,
    string                      ChannelId,
    string                      SourceNodeId,
    string                      TargetNodeId,
    int                         RowCount,
    IReadOnlyList<EventPayload> Events);
