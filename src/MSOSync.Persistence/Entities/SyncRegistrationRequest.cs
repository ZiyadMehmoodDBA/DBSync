using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncRegistrationRequest
{
    public long              RequestId        { get; set; }
    public string            NodeId           { get; set; } = null!;   // ExternalId
    public string            NodeName         { get; set; } = null!;
    public string?           NodeGroup        { get; set; }
    public string?           SyncUrl          { get; set; }
    public string?           NodeVersion      { get; set; }
    public string?           DbType           { get; set; }
    public DateTime?         RequestTime      { get; set; }             // ReceivedAt
    public bool              Approved         { get; set; }             // legacy, superseded by Status
    public string?           MetadataJson     { get; set; }
    public RegistrationType  RegistrationType { get; set; } = RegistrationType.New;
    public RegistrationStatus Status          { get; set; } = RegistrationStatus.Pending;
    public DateTime?         ProcessedAt      { get; set; }
    public string?           ProcessedBy      { get; set; }
    public byte[]            RowVersion       { get; set; } = null!;
}

public enum RegistrationType  { New, ReRegistration, Recovery }
public enum RegistrationStatus { Pending, Approved, Rejected }
