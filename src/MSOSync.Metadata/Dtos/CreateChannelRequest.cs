namespace MSOSync.Metadata.Dtos;

public sealed record CreateChannelRequest(
    string ChannelId,
    int Priority,
    int BatchSize = 1000,
    int MaxBatchToSend = 10,
    long MaxDataSize = 1048576L);
