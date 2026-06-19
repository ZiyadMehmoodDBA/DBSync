namespace MSOSync.Metadata.Descriptors;

public sealed record ParameterDescriptor(
    string Name,
    string Description,
    bool IsSecret,
    bool RequiresRestart,
    bool IsDynamic)
{
    public static readonly IReadOnlyDictionary<string, ParameterDescriptor> Catalog =
        new Dictionary<string, ParameterDescriptor>
        {
            ["sync.interval.seconds"]     = new("sync.interval.seconds",     "How often the sync engine runs (seconds)",    false, true,  true),
            ["sync.batch.size"]           = new("sync.batch.size",           "Max events per batch",                        false, false, true),
            ["sync.max.batch.to.send"]    = new("sync.max.batch.to.send",    "Max batches sent per sync cycle",             false, false, true),
            ["sync.retention.days"]       = new("sync.retention.days",       "Purge terminal batches older than N days",    false, false, true),
            ["sync.audit.retention.days"] = new("sync.audit.retention.days", "Purge audit rows older than N days",          false, false, true),
            ["sync.max.retries"]          = new("sync.max.retries",          "Max retry attempts before batch stays ERROR", false, false, true),
        };

    public static ParameterDescriptor Unknown(string name) =>
        new(name, "Unknown parameter", false, false, true);
}
