namespace MSOSync.Metadata.Users;

public sealed record CreateUserRequest(
    string Username,
    string Password,
    bool   Enabled = true);
