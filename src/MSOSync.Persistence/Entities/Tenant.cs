using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public enum TenantStatus { Provisioning, Active, Suspended, Deleted }
public enum MemberStatus  { Active, Suspended }

[GlobalEntity]
public class Tenant
{
    public Guid            TenantId       { get; set; }
    public string          Name           { get; set; } = "";
    public string          Slug           { get; set; } = "";   // lowercase, [a-z0-9-], immutable after create
    public TenantStatus    Status         { get; set; }
    public EditionType Edition { get; set; }
    public Guid?           LicenseId      { get; set; }         // wired in 15B
    public DateTimeOffset  CreatedAtUtc   { get; set; }
    public DateTimeOffset  UpdatedAtUtc   { get; set; }
    public DateTimeOffset? SuspendedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc   { get; set; }
    public byte[]          RowVersion     { get; set; } = [];
}
