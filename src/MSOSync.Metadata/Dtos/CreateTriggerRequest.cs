namespace MSOSync.Metadata.Dtos;

public sealed record CreateTriggerRequest(
    string TriggerId,
    string SourceTable,
    string ChannelId,
    bool SyncOnInsert = true,
    bool SyncOnUpdate = true,
    bool SyncOnDelete = true);
