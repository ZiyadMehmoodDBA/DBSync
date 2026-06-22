namespace MSOSync.Transport.Payloads;

public sealed record AckPayload(
    long            BatchId,
    long            BatchSequence,
    string          AckNodeId,
    bool            Success,
    string?         ErrorCode,
    DateTimeOffset  AckTime);
