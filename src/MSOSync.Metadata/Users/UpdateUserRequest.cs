namespace MSOSync.Metadata.Users;

public sealed record UpdateUserRequest(
    bool?   Enabled,
    string? NewPassword);
