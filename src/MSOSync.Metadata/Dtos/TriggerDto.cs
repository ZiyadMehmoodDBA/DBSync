namespace MSOSync.Metadata.Dtos;

public sealed record TriggerDto(
    string TriggerId,
    string SourceTable,
    string ChannelId,
    bool SyncOnInsert,
    bool SyncOnUpdate,
    bool SyncOnDelete,
    bool Enabled,
    int TriggerVersion,
    DateTime? LastVerifiedTime);
