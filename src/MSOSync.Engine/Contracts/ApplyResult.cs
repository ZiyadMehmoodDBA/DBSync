namespace MSOSync.Engine;

public sealed record ApplyResult(
    bool    Success,
    int     AppliedRows,
    int     ErrorRows,
    string? ErrorMessage);
