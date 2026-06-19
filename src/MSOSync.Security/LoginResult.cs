namespace MSOSync.Security;

public sealed record LoginResult(
    bool Success,
    string? AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    string? Error);
