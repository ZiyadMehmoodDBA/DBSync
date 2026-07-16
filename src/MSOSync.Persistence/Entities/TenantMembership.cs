using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[GlobalEntity]
public class TenantMembership
{
    public Guid           TenantId       { get; set; }
    public long           UserId         { get; set; }
    public long           RoleId         { get; set; }
    public MemberStatus   Status         { get; set; }
    public DateTimeOffset JoinedAt       { get; set; }
    public DateTimeOffset LastAccessedAt { get; set; }
    public byte[]         RowVersion     { get; set; } = [];

    public Tenant?         Tenant { get; set; }
    public SyncUser?       User   { get; set; }
    public SyncRole?       Role   { get; set; }
}
