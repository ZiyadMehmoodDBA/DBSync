namespace MSOSync.Transport.Payloads;

public sealed record PushResponse(
    long    BatchId,
    bool    Success,
    int     AppliedRows,
    int     ErrorRows,
    string? ErrorMessage);
