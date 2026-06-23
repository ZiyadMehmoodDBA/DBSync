namespace MSOSync.Engine;

public sealed record EventPayload(
    long    EventId,
    string  TriggerId,
    string  EventType,
    string  TableName,
    string? TransactionId,
    string? PkData,
    string? RowData);
