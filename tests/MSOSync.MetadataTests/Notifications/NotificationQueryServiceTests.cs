using FluentAssertions;
using MSOSync.Metadata.Notifications;
using MSOSync.Metadata.Pagination;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Notifications;

public sealed class NotificationQueryServiceTests : IDisposable
{
    private static readonly CursorSigner _signer = new(new byte[32]);
    private readonly AppDbContext _db;
    private readonly NotificationQueryService _sut;

    public NotificationQueryServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new NotificationQueryService(_db, _signer);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(SyncUser user, SyncNotification[] notifications)> SeedAsync(int count = 3)
    {
        var user = new SyncUser { Username = "u1", PasswordHash = "x", Enabled = true, CreatedTime = DateTime.UtcNow };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var notifications = Enumerable.Range(1, count).Select(i => new SyncNotification
        {
            EventType       = "WorkerFailed",
            Severity        = "Critical",
            Title           = $"Notif {i}",
            Body            = "body",
            CreatedAt       = DateTime.UtcNow.AddMinutes(-i),
            LastOccurredAt  = DateTime.UtcNow.AddMinutes(-i),
        }).ToArray();
        _db.Notifications.AddRange(notifications);
        await _db.SaveChangesAsync();

        _db.UserNotifications.AddRange(notifications.Select(n => new SyncUserNotification
        {
            UserId = user.UserId, NotificationId = n.NotificationId
        }));
        await _db.SaveChangesAsync();

        return (user, notifications);
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsDescendingOrder()
    {
        var (user, _) = await SeedAsync(3);
        var result = await _sut.GetPagedAsync(user.UserId, null, 10, false, null, default);
        result.Items.Should().HaveCount(3);
        result.Items.Select(x => x.NotificationId)
            .Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetPagedAsync_Cursor_ReturnsNextPage()
    {
        var (user, _) = await SeedAsync(3);

        var page1 = await _sut.GetPagedAsync(user.UserId, null, 2, false, null, default);
        page1.Items.Should().HaveCount(2);
        page1.NextCursor.Should().NotBeNull();

        var page2 = await _sut.GetPagedAsync(user.UserId, page1.NextCursor, 2, false, null, default);
        page2.Items.Should().HaveCount(1);
        page2.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetPagedAsync_UnreadOnly_FiltersRead()
    {
        var (user, notifications) = await SeedAsync(3);
        var first = await _db.UserNotifications
            .FindAsync(user.UserId, notifications[0].NotificationId);
        first!.IsRead = true;
        await _db.SaveChangesAsync();

        var result = await _sut.GetPagedAsync(user.UserId, null, 10, true, null, default);
        result.Items.Should().HaveCount(2);
        result.Items.Should().NotContain(x => x.NotificationId == notifications[0].NotificationId);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsCorrectCount()
    {
        var (user, notifications) = await SeedAsync(3);
        var count = await _sut.GetUnreadCountAsync(user.UserId, default);
        count.Should().Be(3);
    }

    [Fact]
    public async Task MarkReadAsync_SetsIsReadAndReadAt()
    {
        var (user, notifications) = await SeedAsync(1);

        await _sut.MarkReadAsync(user.UserId, notifications[0].NotificationId, default);

        var row = await _db.UserNotifications
            .FindAsync(user.UserId, notifications[0].NotificationId);
        row!.IsRead.Should().BeTrue();
        row.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkReadAsync_AlreadyRead_IsIdempotent()
    {
        var (user, notifications) = await SeedAsync(1);
        await _sut.MarkReadAsync(user.UserId, notifications[0].NotificationId, default);
        var act = async () => await _sut.MarkReadAsync(user.UserId, notifications[0].NotificationId, default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MarkAllReadAsync_SetsAllUnread()
    {
        var (user, _) = await SeedAsync(3);
        await _sut.MarkAllReadAsync(user.UserId, default);

        var count = await _sut.GetUnreadCountAsync(user.UserId, default);
        count.Should().Be(0);
    }

    [Fact]
    public async Task MarkAllReadAsync_OnlyAffectsRequestingUser()
    {
        var (user1, _) = await SeedAsync(2);

        var user2 = new SyncUser { Username = "u2", PasswordHash = "x", Enabled = true, CreatedTime = DateTime.UtcNow };
        _db.Users.Add(user2);
        await _db.SaveChangesAsync();
        var notif = new SyncNotification { EventType = "WorkerFailed", Severity = "Critical", Title = "T", Body = "B", CreatedAt = DateTime.UtcNow, LastOccurredAt = DateTime.UtcNow };
        _db.Notifications.Add(notif);
        await _db.SaveChangesAsync();
        _db.UserNotifications.Add(new SyncUserNotification { UserId = user2.UserId, NotificationId = notif.NotificationId });
        await _db.SaveChangesAsync();

        await _sut.MarkAllReadAsync(user1.UserId, default);

        var u2Count = await _sut.GetUnreadCountAsync(user2.UserId, default);
        u2Count.Should().Be(1);
    }

    [Fact]
    public async Task ResolveUserIdAsync_ReturnsCorrectId()
    {
        var (user, _) = await SeedAsync(0);
        var id = await _sut.ResolveUserIdAsync(user.Username, default);
        id.Should().Be(user.UserId);
    }
}
