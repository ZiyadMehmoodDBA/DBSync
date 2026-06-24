namespace MSOSync.Metadata.Users;

public sealed record UserDetailDto(
    long      UserId,
    string    Username,
    bool      Enabled,
    DateTime? LastLogin,
    int       FailedAttempts,
    DateTime? LockedUntil,
    DateTime? PasswordChangedAt,
    DateTime? CreatedTime);
