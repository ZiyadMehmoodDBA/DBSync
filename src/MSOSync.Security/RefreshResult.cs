namespace MSOSync.Security;

public sealed record RefreshResult(
    bool Success,
    string? AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    string? Error);
