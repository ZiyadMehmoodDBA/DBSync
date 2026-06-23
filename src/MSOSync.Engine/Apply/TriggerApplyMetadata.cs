namespace MSOSync.Engine;

public sealed record TriggerApplyMetadata(
    string                SchemaName,
    string                TableName,
    IReadOnlyList<string> PkColumns,
    int                   TriggerVersion);
