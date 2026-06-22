namespace MSOSync.Transport.Payloads;

// EventType: char 'I'/'U'/'D' from SyncDataEvent mapped to "INSERT"/"UPDATE"/"DELETE"
public sealed record EventPayload(
    long    EventId,
    string  TriggerId,
    string  EventType,
    string  TableName,
    string? TransactionId,
    string? PkData,
    string? RowData);
