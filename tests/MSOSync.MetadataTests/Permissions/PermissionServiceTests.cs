using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Permissions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Permissions;

public sealed class PermissionServiceTests : IDisposable
{
    private readonly AppDbContext               _db;
    private readonly IMemoryCache               _cache;
    private readonly Mock<IMediator>            _mediator = new();
    private readonly Mock<ICurrentUserService>  _currentUser = new();
    private readonly PermissionService          _sut;

    public PermissionServiceTests()
    {
        _db    = TestDbContext.Create();
        _cache = new MemoryCache(new MemoryCacheOptions());

        // Seed roles
        _db.Roles.AddRange(
            new SyncRole { RoleId = 1, RoleName = "VIEWER"   },
            new SyncRole { RoleId = 2, RoleName = "OPERATOR"  },
            new SyncRole { RoleId = 3, RoleName = "ADMIN"     });
        // Seed viewer user
        _db.Users.Add(new SyncUser
        {
            UserId = 1, Username = "alice", PasswordHash = "h",
            Enabled = true, PasswordChangedAt = DateTime.UtcNow, CreatedTime = DateTime.UtcNow,
        });
        _db.UserRoles.Add(new SyncUserRole { UserId = 1, RoleId = 1 }); // alice = VIEWER
        // Seed all 12 permissions
        _db.Permissions.AddRange(
            new SyncPermission { PermissionKey = "VIEW_EVENTS",     DisplayName = "View Events",     Category = "DATA",           SortOrder = 10 },
            new SyncPermission { PermissionKey = "VIEW_METRICS",    DisplayName = "View Metrics",    Category = "DATA",           SortOrder = 20 },
            new SyncPermission { PermissionKey = "VIEW_AUDIT",      DisplayName = "View Audit",      Category = "DATA",           SortOrder = 30 },
            new SyncPermission { PermissionKey = "VIEW_TOPOLOGY",   DisplayName = "View Topology",   Category = "DATA",           SortOrder = 40 },
            new SyncPermission { PermissionKey = "EXPORT_DATA",     DisplayName = "Export Data",     Category = "DATA",           SortOrder = 50 },
            new SyncPermission { PermissionKey = "RETRY_BATCHES",   DisplayName = "Retry Batches",   Category = "OPERATIONS",     SortOrder = 10 },
            new SyncPermission { PermissionKey = "APPROVE_NODES",   DisplayName = "Approve Nodes",   Category = "OPERATIONS",     SortOrder = 20 },
            new SyncPermission { PermissionKey = "RELEASE_LOCKS",   DisplayName = "Release Locks",   Category = "OPERATIONS",     SortOrder = 30 },
            new SyncPermission { PermissionKey = "EDIT_PARAMETERS", DisplayName = "Edit Parameters", Category = "CONFIGURATION",  SortOrder = 10 },
            new SyncPermission { PermissionKey = "MANAGE_TRIGGERS", DisplayName = "Manage Triggers", Category = "CONFIGURATION",  SortOrder = 20 },
            new SyncPermission { PermissionKey = "MANAGE_ROUTERS",  DisplayName = "Manage Routers",  Category = "CONFIGURATION",  SortOrder = 30 },
            new SyncPermission { PermissionKey = "MANAGE_USERS",    DisplayName = "Manage Users",    Category = "ADMINISTRATION", SortOrder = 10 });
        // Seed default VIEWER permissions
        _db.RolePermissions.AddRange(
            new SyncRolePermission { RoleName = "VIEWER", PermissionKey = "VIEW_EVENTS"   },
            new SyncRolePermission { RoleName = "VIEWER", PermissionKey = "VIEW_METRICS"  },
            new SyncRolePermission { RoleName = "VIEWER", PermissionKey = "VIEW_AUDIT"    },
            new SyncRolePermission { RoleName = "VIEWER", PermissionKey = "VIEW_TOPOLOGY" });
        // Seed OPERATOR permissions
        _db.RolePermissions.AddRange(
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "VIEW_EVENTS"      },
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "VIEW_METRICS"     },
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "VIEW_AUDIT"       },
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "VIEW_TOPOLOGY"    },
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "EXPORT_DATA"      },
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "RETRY_BATCHES"    },
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "APPROVE_NODES"    },
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "RELEASE_LOCKS"    },
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "EDIT_PARAMETERS"  },
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "MANAGE_TRIGGERS"  },
            new SyncRolePermission { RoleName = "OPERATOR", PermissionKey = "MANAGE_ROUTERS"   });
        // Seed ADMIN permissions (all 12)
        _db.RolePermissions.AddRange(
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "VIEW_EVENTS"     },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "VIEW_METRICS"    },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "VIEW_AUDIT"      },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "VIEW_TOPOLOGY"   },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "EXPORT_DATA"     },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "RETRY_BATCHES"   },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "APPROVE_NODES"   },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "RELEASE_LOCKS"   },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "EDIT_PARAMETERS" },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "MANAGE_TRIGGERS" },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "MANAGE_ROUTERS"  },
            new SyncRolePermission { RoleName = "ADMIN", PermissionKey = "MANAGE_USERS"    });
        _db.SaveChanges();

        _currentUser.Setup(c => c.GetCurrentUsername()).Returns("test-user");
        _sut = new PermissionService(_db, _cache, _mediator.Object, _currentUser.Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetEffective_Viewer_ReturnsFourViewPermissions()
    {
        var result = await _sut.GetEffectivePermissionsAsync("alice");
        result.Role.Should().Be("VIEWER");
        result.Permissions.Should().BeEquivalentTo(
            ["VIEW_EVENTS", "VIEW_METRICS", "VIEW_AUDIT", "VIEW_TOPOLOGY"]);
    }

    [Fact]
    public async Task GetEffective_Operator_ReturnsElevenPermissions()
    {
        // Add operator user
        _db.Users.Add(new SyncUser { UserId = 2, Username = "bob", PasswordHash = "h",
            Enabled = true, PasswordChangedAt = DateTime.UtcNow, CreatedTime = DateTime.UtcNow });
        _db.UserRoles.Add(new SyncUserRole { UserId = 2, RoleId = 2 });
        await _db.SaveChangesAsync();

        var result = await _sut.GetEffectivePermissionsAsync("bob");
        result.Role.Should().Be("OPERATOR");
        result.Permissions.Should().HaveCount(11);
        result.Permissions.Should().Contain("RETRY_BATCHES");
    }

    [Fact]
    public async Task GetEffective_Admin_ReturnsAllTwelve()
    {
        _db.Users.Add(new SyncUser { UserId = 3, Username = "carol", PasswordHash = "h",
            Enabled = true, PasswordChangedAt = DateTime.UtcNow, CreatedTime = DateTime.UtcNow });
        _db.UserRoles.Add(new SyncUserRole { UserId = 3, RoleId = 3 });
        await _db.SaveChangesAsync();

        var result = await _sut.GetEffectivePermissionsAsync("carol");
        result.Permissions.Should().HaveCount(12);
    }

    [Fact]
    public async Task Grant_AddsPermissionToRole()
    {
        await _sut.GrantPermissionAsync("VIEWER", "EXPORT_DATA");

        var result = await _sut.GetEffectivePermissionsAsync("alice");
        result.Permissions.Should().Contain("EXPORT_DATA");
    }

    [Fact]
    public async Task Revoke_RemovesPermissionFromRole()
    {
        await _sut.RevokePermissionAsync("VIEWER", "VIEW_EVENTS");

        var result = await _sut.GetEffectivePermissionsAsync("alice");
        result.Permissions.Should().NotContain("VIEW_EVENTS");
    }

    [Fact]
    public async Task Revoke_ManageUsers_FromAdmin_ThrowsValidationException()
    {
        await _sut.Invoking(s => s.RevokePermissionAsync("ADMIN", "MANAGE_USERS"))
            .Should().ThrowAsync<ValidationException>()
            .WithMessage("*PERMISSION_PROTECTED*");
    }

    [Fact]
    public async Task Reset_RestoresDefaultPermissions()
    {
        // Grant VIEWER an extra permission
        await _sut.GrantPermissionAsync("VIEWER", "EXPORT_DATA");
        // Revoke a default permission
        await _sut.RevokePermissionAsync("VIEWER", "VIEW_EVENTS");

        await _sut.ResetRoleToDefaultsAsync("VIEWER");

        var result = await _sut.GetEffectivePermissionsAsync("alice");
        result.Permissions.Should().BeEquivalentTo(
            ["VIEW_EVENTS", "VIEW_METRICS", "VIEW_AUDIT", "VIEW_TOPOLOGY"]);
    }

    [Fact]
    public async Task CopyFrom_ReplacesTargetPermissions()
    {
        // VIEWER gets OPERATOR's full set, loses its own unique ones
        await _sut.CopyPermissionsFromAsync("VIEWER", "OPERATOR");

        var result = await _sut.GetEffectivePermissionsAsync("alice");
        result.Permissions.Should().HaveCount(11);
        result.Permissions.Should().Contain("RETRY_BATCHES");
    }

    [Fact]
    public async Task CopyFrom_IsIdempotent()
    {
        await _sut.CopyPermissionsFromAsync("VIEWER", "OPERATOR");
        await _sut.CopyPermissionsFromAsync("VIEWER", "OPERATOR");

        var result = await _sut.GetEffectivePermissionsAsync("alice");
        result.Permissions.Should().HaveCount(11);
    }

    [Fact]
    public async Task GetAllPermissions_ReturnsAllTwelveCatalogEntries()
    {
        var result = await _sut.GetAllPermissionsAsync();
        result.Should().HaveCount(12);
        result.Should().AllSatisfy(p =>
        {
            p.PermissionKey.Should().NotBeNullOrEmpty();
            p.DisplayName.Should().NotBeNullOrEmpty();
            p.Category.Should().BeOneOf("DATA", "OPERATIONS", "CONFIGURATION", "ADMINISTRATION");
        });
    }

    [Fact]
    public async Task Grant_WritesAuditEntry()
    {
        await _sut.GrantPermissionAsync("VIEWER", "EXPORT_DATA");

        var audit = _db.Audits.FirstOrDefault(a => a.ActionName == "GRANT_PERMISSION");
        audit.Should().NotBeNull();
        audit!.ObjectName.Should().Contain("EXPORT_DATA");
        audit.Username.Should().Be("test-user");
    }

    [Fact]
    public async Task Revoke_WritesAuditEntry()
    {
        await _sut.RevokePermissionAsync("VIEWER", "VIEW_EVENTS");

        var audit = _db.Audits.FirstOrDefault(a => a.ActionName == "REVOKE_PERMISSION");
        audit.Should().NotBeNull();
    }

    [Fact]
    public async Task Grant_EvictsCacheForRole()
    {
        // Prime the cache
        await _sut.GetEffectivePermissionsAsync("alice");
        _cache.TryGetValue("permissions:VIEWER", out _).Should().BeTrue();

        await _sut.GrantPermissionAsync("VIEWER", "EXPORT_DATA");

        _cache.TryGetValue("permissions:VIEWER", out _).Should().BeFalse();
    }
}
