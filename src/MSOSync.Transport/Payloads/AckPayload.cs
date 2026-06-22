namespace MSOSync.Transport.Payloads;

public sealed record AckPayload(
    long            BatchId,
    long            BatchSequence,
    string          NodeId,
    bool            Success,
    string?         ErrorMessage,
    DateTimeOffset  AckTime);
