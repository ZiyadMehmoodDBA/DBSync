namespace MSOSync.Metadata.Dtos;

public sealed record UpdateTriggerRequest(
    string SourceTable,
    string ChannelId,
    bool SyncOnInsert,
    bool SyncOnUpdate,
    bool SyncOnDelete);
