using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public sealed class RegistrationDiffService : IRegistrationDiffService
{
    public RegistrationDiffDto Compute(
        RegistrationMetadataDto incoming,
        SyncNode                currentNode,
        bool                    includeUnchanged = false)
    {
        var items        = new List<RegistrationDiffItemDto>();
        var incomingFlat = FlattenMetadata(incoming);
        var currentFlat  = BuildCurrentFlat(currentNode, incomingFlat);

        var allKeys = incomingFlat.Keys
            .Union(currentFlat.Keys, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var key in allKeys)
        {
            incomingFlat.TryGetValue(key, out var inVal);
            currentFlat.TryGetValue(key, out var curVal);

            var changeType = (curVal, inVal) switch
            {
                (null, null)           => RegistrationChangeType.Unchanged,
                (null, not null)       => RegistrationChangeType.Added,
                (not null, null)       => RegistrationChangeType.Removed,
                _ when curVal == inVal => RegistrationChangeType.Unchanged,
                _                      => RegistrationChangeType.Modified,
            };

            if (changeType != RegistrationChangeType.Unchanged || includeUnchanged)
                items.Add(new RegistrationDiffItemDto(key, curVal, inVal, changeType));
        }

        return new RegistrationDiffDto(items.AsReadOnly());
    }

    /// <summary>
    /// Builds the baseline ("current") side of the comparison.
    /// Always seeds all 13 known metadata fields with null so that
    /// Unchanged comparisons are possible (useful for includeUnchanged=true).
    /// Node-stored values (e.g. DbName → Database.InstanceName) are overlaid
    /// only when the incoming payload contains at least one metadata section,
    /// which prevents spurious "Removed" entries when a re-registering node
    /// chooses to send no metadata at all.
    /// </summary>
    private static Dictionary<string, string?> BuildCurrentFlat(
        SyncNode node, Dictionary<string, string?> incomingFlat)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Machine.HostName"]           = null,
            ["Machine.OsVersion"]          = null,
            ["Machine.MachineName"]        = null,
            ["Database.Edition"]           = null,
            ["Database.Version"]           = null,
            ["Database.Collation"]         = null,
            ["Database.InstanceName"]      = null,
            ["Application.AgentVersion"]   = null,
            ["Application.RuntimeVersion"] = null,
            ["Application.InstallPath"]    = null,
            ["Hardware.CpuCount"]          = null,
            ["Hardware.RamBytes"]          = null,
            ["Hardware.DiskBytes"]         = null,
        };

        // Overlay node-stored DB fields only when the incoming payload has any metadata.
        // An empty payload means "nothing changed" — no diff is generated.
        if (incomingFlat.Count > 0 && node.DbName is not null)
            d["Database.InstanceName"] = node.DbName;

        return d;
    }

    private static Dictionary<string, string?> FlattenMetadata(RegistrationMetadataDto m)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (m.Machine is { } mac)
        {
            d["Machine.HostName"]    = mac.HostName;
            d["Machine.OsVersion"]   = mac.OsVersion;
            d["Machine.MachineName"] = mac.MachineName;
        }
        if (m.Database is { } db)
        {
            d["Database.Edition"]      = db.Edition;
            d["Database.Version"]      = db.Version;
            d["Database.Collation"]    = db.Collation;
            d["Database.InstanceName"] = db.InstanceName;
        }
        if (m.Application is { } app)
        {
            d["Application.AgentVersion"]   = app.AgentVersion;
            d["Application.RuntimeVersion"] = app.RuntimeVersion;
            d["Application.InstallPath"]    = app.InstallPath;
        }
        if (m.Hardware is { } hw)
        {
            d["Hardware.CpuCount"] = hw.CpuCount?.ToString();
            d["Hardware.RamBytes"] = hw.RamBytes?.ToString();
            d["Hardware.DiskBytes"] = hw.DiskBytes?.ToString();
        }
        return d;
    }
}
