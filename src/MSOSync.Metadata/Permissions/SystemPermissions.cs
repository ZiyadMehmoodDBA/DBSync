namespace MSOSync.Metadata.Permissions;

public static class SystemPermissions
{
    public const string ManageUsers = "MANAGE_USERS";

    // Default permission sets per role — used by ResetRoleToDefaultsAsync
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Defaults =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["VIEWER"]   = ["VIEW_EVENTS", "VIEW_METRICS", "VIEW_AUDIT", "VIEW_TOPOLOGY"],
            ["OPERATOR"] = ["VIEW_EVENTS", "VIEW_METRICS", "VIEW_AUDIT", "VIEW_TOPOLOGY",
                            "EXPORT_DATA", "RETRY_BATCHES", "APPROVE_NODES", "RELEASE_LOCKS",
                            "EDIT_PARAMETERS", "MANAGE_TRIGGERS", "MANAGE_ROUTERS"],
            ["ADMIN"]    = ["VIEW_EVENTS", "VIEW_METRICS", "VIEW_AUDIT", "VIEW_TOPOLOGY",
                            "EXPORT_DATA", "RETRY_BATCHES", "APPROVE_NODES", "RELEASE_LOCKS",
                            "EDIT_PARAMETERS", "MANAGE_TRIGGERS", "MANAGE_ROUTERS", "MANAGE_USERS"],
        };
}
