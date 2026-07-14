using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using MSOSync.Metadata.Notifications;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Notifications;

public sealed class NotificationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IPublisher> _publisher;
    private readonly NotificationService _sut;

    public NotificationServiceTests()
    {
        _db        = TestDbContext.Create();
        _publisher = new Mock<IPublisher>();
        _sut       = new NotificationService(_db, _publisher.Object);
    }

    public void Dispose() => _db.Dispose();

    // Seed helpers
    private async Task<SyncUser> SeedUserAsync(string username, string roleName)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName)
                   ?? _db.Roles.Add(new SyncRole { RoleName = roleName }).Entity;
        await _db.SaveChangesAsync();

        var user = new SyncUser { Username = username, PasswordHash = "x", Enabled = true, CreatedTime = DateTime.UtcNow };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.UserRoles.Add(new SyncUserRole { UserId = user.UserId, RoleId = role.RoleId });
        await _db.SaveChangesAsync();

        return user;
    }

    [Fact]
    public async Task CreateAsync_AllUsers_FansOutToAllEnabledUsers()
    {
        var u1 = await SeedUserAsync("viewer1", "VIEWER");
        var u2 = await SeedUserAsync("admin1",  "ADMIN");

        await _sut.CreateAsync(
            NotificationEventType.WorkerFailed, NotificationSeverity.Critical,
            "Worker down", "HeartbeatWorker failed",
            "Worker", "HeartbeatWorker", null,
            NotificationAudience.AllUsers);

        var rows = await _db.UserNotifications.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.UserId).Should().BeEquivalentTo(new[] { u1.UserId, u2.UserId });
    }

    [Fact]
    public async Task CreateAsync_Operators_SkipsViewerRole()
    {
        await SeedUserAsync("viewer2", "VIEWER");
        var op    = await SeedUserAsync("op1",    "OPERATOR");
        var admin = await SeedUserAsync("admin4", "ADMIN");

        await _sut.CreateAsync(
            NotificationEventType.NodeInRecovery, NotificationSeverity.Warning,
            "Node in recovery", "node-x is in recovery",
            "Node", "node-x", null,
            NotificationAudience.Operators);

        var rows = await _db.UserNotifications.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.UserId).Should().BeEquivalentTo(new[] { op.UserId, admin.UserId });
    }

    [Fact]
    public async Task CreateAsync_Administrators_OnlyAdminRole()
    {
        await SeedUserAsync("op2", "OPERATOR");
        var admin = await SeedUserAsync("admin2", "ADMIN");

        await _sut.CreateAsync(
            NotificationEventType.AccountLocked, NotificationSeverity.Security,
            "Account locked", "user alice was locked",
            null, null, null,
            NotificationAudience.Administrators);

        var rows = await _db.UserNotifications.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].UserId.Should().Be(admin.UserId);
    }

    [Fact]
    public async Task CreateAsync_Dedup_SameKeyWithinWindow_IncrementsCount_NoNewRow()
    {
        await SeedUserAsync("viewer3", "VIEWER");

        await _sut.CreateAsync(
            NotificationEventType.WorkerFailed, NotificationSeverity.Critical,
            "Worker down", "Body", "Worker", "HeartbeatWorker", null,
            NotificationAudience.AllUsers);

        await _sut.CreateAsync(
            NotificationEventType.WorkerFailed, NotificationSeverity.Critical,
            "Worker down again", "Body2", "Worker", "HeartbeatWorker", null,
            NotificationAudience.AllUsers);

        var notifications = await _db.Notifications.ToListAsync();
        notifications.Should().ContainSingle();
        notifications[0].OccurrenceCount.Should().Be(2);

        // Fan-out should only have happened once
        var userRows = await _db.UserNotifications.ToListAsync();
        userRows.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateAsync_Dedup_NullEntityId_NeverDedupes()
    {
        await SeedUserAsync("admin3", "ADMIN");

        // Both have null sourceEntityId → dedup key is null → always create
        await _sut.CreateAsync(
            NotificationEventType.AccountLocked, NotificationSeverity.Security,
            "Account locked", "alice", null, null, null,
            NotificationAudience.AllUsers);

        await _sut.CreateAsync(
            NotificationEventType.AccountLocked, NotificationSeverity.Security,
            "Account locked", "bob", null, null, null,
            NotificationAudience.AllUsers);

        var notifications = await _db.Notifications.ToListAsync();
        notifications.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_PublishesDomainEvent_WithCorrectUserIds()
    {
        var u = await SeedUserAsync("viewer4", "VIEWER");

        await _sut.CreateAsync(
            NotificationEventType.NodeRejected, NotificationSeverity.Info,
            "Node rejected", "node-y was rejected",
            "Node", "node-y", null,
            NotificationAudience.AllUsers);

        _publisher.Verify(p => p.Publish(
            It.Is<NotificationCreatedDomainEvent>(e =>
                e.UserIds.Contains(u.UserId) && e.PushDto.Title == "Node rejected"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DisabledUser_Excluded()
    {
        var disabled = new SyncUser { Username = "disabled1", PasswordHash = "x", Enabled = false, CreatedTime = DateTime.UtcNow };
        _db.Users.Add(disabled);
        await _db.SaveChangesAsync();
        var role = _db.Roles.Add(new SyncRole { RoleName = "VIEWER" }).Entity;
        await _db.SaveChangesAsync();
        _db.UserRoles.Add(new SyncUserRole { UserId = disabled.UserId, RoleId = role.RoleId });
        await _db.SaveChangesAsync();

        await _sut.CreateAsync(
            NotificationEventType.WorkerFailed, NotificationSeverity.Critical,
            "t", "b", null, null, null, NotificationAudience.AllUsers);

        var rows = await _db.UserNotifications.ToListAsync();
        rows.Should().BeEmpty();
        (await _db.Notifications.ToListAsync()).Should().BeEmpty();
    }
}
