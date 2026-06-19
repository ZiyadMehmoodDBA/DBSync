namespace MSOSync.Metadata.Dtos;

public sealed record UpdateChannelRequest(
    int Priority,
    int BatchSize,
    int MaxBatchToSend,
    long MaxDataSize);
