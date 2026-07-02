using MediatR;

namespace MSOSync.Metadata.Permissions;

public sealed record PermissionChangedNotification(
    string RoleName,
    string Action,        // "Grant" | "Revoke" | "Reset" | "Copy"
    DateTimeOffset OccurredAt) : INotification;
