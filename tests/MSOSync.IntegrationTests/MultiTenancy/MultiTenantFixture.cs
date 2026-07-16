// tests/MSOSync.IntegrationTests/MultiTenancy/MultiTenantFixture.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.MultiTenancy;

/// <summary>
/// Seeds two tenants (TenantA + TenantB) with nodes and channels for isolation tests.
/// Uses a dedicated test database to avoid conflicts with DatabaseFixture.
/// </summary>
public sealed class MultiTenantFixture : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSync_MultiTenancy_Tests;Trusted_Connection=True;TrustServerCertificate=True;";

    /// <summary>
    /// Shared mutable accessor — the single compiled EF model closes over this.
    /// Tests set TenantId on this accessor before issuing queries.
    /// </summary>
    public MutableTenantAccessor Accessor { get; } = new();

    /// <summary>
    /// Single DbContext instance used for all seeding and fixture-level operations.
    /// Its model is compiled once with the mutable accessor so query filters work.
    /// </summary>
    public AppDbContext Db { get; private set; } = null!;

    public Guid TenantAId    { get; } = Guid.NewGuid();
    public Guid TenantBId    { get; } = Guid.NewGuid();
    public long UserAId      { get; private set; }
    public long UserBId      { get; private set; }
    public long AdminRoleId  { get; private set; }

    public string NodeAId    { get; } = $"node-a-{Guid.NewGuid():N}";
    public string NodeBId    { get; } = $"node-b-{Guid.NewGuid():N}";
    public string ChannelAId { get; } = $"chan-a-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        // Build options referencing our shared accessor — this ensures the compiled
        // EF model has query filters that evaluate Accessor.TenantId at query time.
        var opts = AppDbContext.CreateOptionsBuilder(ConnectionString).Options;
        Db = new AppDbContext(opts, Accessor);
        await Db.Database.MigrateAsync();

        // Queries during seeding use Accessor.TenantId == null (platform/bypass mode).
        // The accessor starts as null (no filter), so all seeding queries are unfiltered.

        // Get the ADMIN role id (seeded in migrations)
        var adminRole = await Db.Roles.FirstAsync(r => r.RoleName == "ADMIN");
        AdminRoleId = adminRole.RoleId;

        // Seed TenantA
        Db.Tenants.Add(new Tenant
        {
            TenantId     = TenantAId,
            Name         = "Tenant A",
            Slug         = $"tenant-a-{TenantAId:N}",
            Status       = TenantStatus.Active,
            Edition      = EditionType.Community,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        // Seed TenantB
        Db.Tenants.Add(new Tenant
        {
            TenantId     = TenantBId,
            Name         = "Tenant B",
            Slug         = $"tenant-b-{TenantBId:N}",
            Status       = TenantStatus.Active,
            Edition      = EditionType.Community,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await Db.SaveChangesAsync();

        // Seed users
        var userA = new SyncUser { Username = $"user-a-{TenantAId:N}", PasswordHash = "x", Enabled = true };
        var userB = new SyncUser { Username = $"user-b-{TenantBId:N}", PasswordHash = "x", Enabled = true };
        Db.Users.AddRange(userA, userB);
        await Db.SaveChangesAsync();

        UserAId = userA.UserId;
        UserBId = userB.UserId;

        // Memberships
        Db.TenantMemberships.AddRange(
            new TenantMembership
            {
                TenantId       = TenantAId, UserId = UserAId, RoleId = AdminRoleId,
                Status         = MemberStatus.Active,
                JoinedAt       = DateTimeOffset.UtcNow, LastAccessedAt = DateTimeOffset.UtcNow,
            },
            new TenantMembership
            {
                TenantId       = TenantBId, UserId = UserBId, RoleId = AdminRoleId,
                Status         = MemberStatus.Active,
                JoinedAt       = DateTimeOffset.UtcNow, LastAccessedAt = DateTimeOffset.UtcNow,
            }
        );

        // Node for TenantA
        Db.Nodes.Add(new SyncNode
        {
            NodeId             = NodeAId, TenantId           = TenantAId,
            GroupId            = "test-group", SyncUrl        = "http://test-a:8080",
            LifecycleState     = NodeLifecycleState.Active,
            ConnectivityStatus = ConnectivityStatus.Reachable,
            RegistrationTime   = DateTime.UtcNow,
        });

        // Node for TenantB
        Db.Nodes.Add(new SyncNode
        {
            NodeId             = NodeBId, TenantId           = TenantBId,
            GroupId            = "test-group", SyncUrl        = "http://test-b:8080",
            LifecycleState     = NodeLifecycleState.Active,
            ConnectivityStatus = ConnectivityStatus.Reachable,
            RegistrationTime   = DateTime.UtcNow,
        });

        // Channel for TenantA
        Db.Channels.Add(new SyncChannel { ChannelId = ChannelAId, TenantId = TenantAId });

        await Db.SaveChangesAsync();
    }

    /// <summary>
    /// Execute an action with the query filter scoped to the given tenant.
    /// Sets Accessor.TenantId = tenantId before the callback and restores it to null after.
    /// </summary>
    public async Task WithTenantAsync(Guid tenantId, Func<Task> action)
    {
        Accessor.TenantId = tenantId;
        try
        {
            Db.ChangeTracker.Clear();   // avoid stale tracked entities
            await action();
        }
        finally
        {
            Accessor.TenantId = null;
            Db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Execute an action with the query filter scoped to a specific tenant,
    /// returning a result.
    /// </summary>
    public async Task<T> WithTenantAsync<T>(Guid tenantId, Func<Task<T>> action)
    {
        Accessor.TenantId = tenantId;
        try
        {
            Db.ChangeTracker.Clear();
            return await action();
        }
        finally
        {
            Accessor.TenantId = null;
            Db.ChangeTracker.Clear();
        }
    }

    public async Task DisposeAsync()
    {
        // Drop the dedicated test database entirely (same pattern as SecurityFixture)
        await Db.Database.EnsureDeletedAsync();
        await Db.DisposeAsync();
    }
}

/// <summary>
/// Mutable ICurrentTenantAccessor — the EF query filter closes over this.
/// Setting TenantId changes which tenant's data is visible on the NEXT query.
/// </summary>
public sealed class MutableTenantAccessor : ICurrentTenantAccessor
{
    public Guid? TenantId { get; set; }
}

/// <summary>Test helper: static ICurrentTenantAccessor that always returns a fixed value.</summary>
public sealed class StaticTenantAccessor(Guid? tenantId) : ICurrentTenantAccessor
{
    public Guid? TenantId => tenantId;
}

/// <summary>
/// xUnit collection definition — all MultiTenancy test classes share a single fixture instance,
/// preventing concurrent database creation races.
/// </summary>
[CollectionDefinition("MultiTenancy")]
public sealed class MultiTenancyCollection : ICollectionFixture<MultiTenantFixture> { }
