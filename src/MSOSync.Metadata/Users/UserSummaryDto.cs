namespace MSOSync.Metadata.Users;

public sealed record UserSummaryDto(
    long      UserId,
    string    Username,
    bool      Enabled,
    DateTime? LastLogin,
    DateTime? LockedUntil);
