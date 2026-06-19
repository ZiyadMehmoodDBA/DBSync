namespace MSOSync.Metadata.Dtos;

public sealed record ChannelDto(
    string ChannelId,
    int Priority,
    int BatchSize,
    int MaxBatchToSend,
    long MaxDataSize,
    bool Enabled);
