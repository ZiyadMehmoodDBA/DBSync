using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncExportJob : ITenantScoped
{
    public Guid     JobId           { get; set; }
    public Guid?    ParentJobId     { get; set; }
    public string   RequestedBy     { get; set; } = string.Empty;
    public string   ResourceType    { get; set; } = string.Empty;
    public string   Format          { get; set; } = string.Empty;
    public string   FiltersJson     { get; set; } = string.Empty;
    public string   Status          { get; set; } = ExportJobStatus.Pending;
    public int      ProgressPercent { get; set; }
    public long?    RowCount        { get; set; }
    public string?  OutputPath      { get; set; }
    public string?  ErrorMessage    { get; set; }
    public DateTimeOffset?  ExpiresAt    { get; set; }
    public DateTimeOffset   CreatedAt    { get; set; }
    public DateTimeOffset?  StartedAt    { get; set; }
    public DateTimeOffset?  CompletedAt  { get; set; }
    public Guid             TenantId     { get; set; }
}

public static class ExportJobStatus
{
    public const string Pending   = "Pending";
    public const string Running   = "Running";
    public const string Completed = "Completed";
    public const string Failed    = "Failed";
    public const string Deleted   = "Deleted";
    public const string Expired   = "Expired";
}
