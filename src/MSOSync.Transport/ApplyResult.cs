namespace MSOSync.Transport;

public sealed record ApplyResult(
    bool    Success,
    int     AppliedRows,
    int     ErrorRows,
    string? ErrorMessage);
