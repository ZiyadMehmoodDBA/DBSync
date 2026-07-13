using FluentAssertions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Pagination;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Audit;

public sealed class AuditQueryServiceTests : IDisposable
{
    private static readonly CursorSigner _signer = new(new byte[32]);

    private readonly AppDbContext      _db;
    private readonly AuditQueryService _sut;

    public AuditQueryServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new AuditQueryService(_db, _signer);
    }

    public void Dispose() => _db.Dispose();

    private static SyncAudit Audit(long id, string? username = null, string? actionName = null,
        string? objectName = null, DateTime? createTime = null) => new()
    {
        AuditId       = id,
        Username      = username    ?? "alice",
        ActionName    = actionName  ?? "UPDATE",
        ObjectName    = objectName  ?? "SyncNode",
        CorrelationId = null,
        CreateTime    = createTime  ?? DateTime.UtcNow,
    };

    // ── GetAuditsAsync ────────────────────────────────────────

    [Fact]
    public async Task GetAudits_NoFilter_ReturnsAll()
    {
        _db.Audits.AddRange(Audit(1), Audit(2), Audit(3));
        await _db.SaveChangesAsync();

        var result = await _sut.GetAuditsAsync(new AuditFilter(), default);

        result.Items.Should().HaveCount(3);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task GetAudits_FilterByUsername_ReturnsMatching()
    {
        _db.Audits.AddRange(
            Audit(1, username: "alice"),
            Audit(2, username: "bob"),
            Audit(3, username: "alice"));
        await _db.SaveChangesAsync();

        var result = await _sut.GetAuditsAsync(
            new AuditFilter { Username = "alice" }, default);

        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(a => a.Username.Should().Be("alice"));
    }

    [Fact]
    public async Task GetAudits_FilterByActionName_ReturnsMatching()
    {
        _db.Audits.AddRange(
            Audit(1, actionName: "UPDATE"),
            Audit(2, actionName: "DELETE"),
            Audit(3, actionName: "UPDATE"));
        await _db.SaveChangesAsync();

        var result = await _sut.GetAuditsAsync(
            new AuditFilter { ActionName = "UPDATE" }, default);

        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAudits_FilterByDateRange_ReturnsMatching()
    {
        var now = DateTime.UtcNow;
        _db.Audits.AddRange(
            Audit(1, createTime: now.AddDays(-2)),
            Audit(2, createTime: now.AddDays(-1)),
            Audit(3, createTime: now));
        await _db.SaveChangesAsync();

        var result = await _sut.GetAuditsAsync(
            new AuditFilter { From = now.AddDays(-1).AddHours(-1), To = now.AddHours(-1) }, default);

        result.Items.Should().HaveCount(1);
        result.Items[0].AuditId.Should().Be(2);
    }

    [Fact]
    public async Task GetAudits_Pagination_HonorsPageSize()
    {
        for (int i = 1; i <= 10; i++)
            _db.Audits.Add(Audit(i, createTime: DateTime.UtcNow.AddMinutes(-i)));
        await _db.SaveChangesAsync();

        var result = await _sut.GetAuditsAsync(
            new AuditFilter { PageSize = 3 }, default);

        result.Items.Should().HaveCount(3);
        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task GetAudits_IncludeTotalCount_ReturnsTotalCount()
    {
        for (int i = 1; i <= 10; i++)
            _db.Audits.Add(Audit(i, createTime: DateTime.UtcNow.AddMinutes(-i)));
        await _db.SaveChangesAsync();

        var result = await _sut.GetAuditsAsync(
            new AuditFilter { PageSize = 3, IncludeTotalCount = true }, default);

        result.TotalCount.Should().Be(10);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAudits_Cursor_ReturnsNextPage()
    {
        for (int i = 1; i <= 5; i++)
            _db.Audits.Add(Audit(i, createTime: DateTime.UtcNow.AddMinutes(-i)));
        await _db.SaveChangesAsync();

        var page1 = await _sut.GetAuditsAsync(new AuditFilter { PageSize = 2 }, default);
        page1.Items.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();

        var page2 = await _sut.GetAuditsAsync(
            new AuditFilter { PageSize = 2, Cursor = page1.NextCursor }, default);
        page2.Items.Should().HaveCount(2);

        var allIds = page1.Items.Select(a => a.AuditId)
            .Concat(page2.Items.Select(a => a.AuditId))
            .ToList();
        allIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetAudits_OrderedByAuditIdDesc()
    {
        var now = DateTime.UtcNow;
        _db.Audits.AddRange(
            Audit(1, createTime: now.AddMinutes(-10)),
            Audit(2, createTime: now.AddMinutes(-5)),
            Audit(3, createTime: now));
        await _db.SaveChangesAsync();

        var result = await _sut.GetAuditsAsync(new AuditFilter(), default);

        result.Items[0].AuditId.Should().Be(3);
        result.Items[1].AuditId.Should().Be(2);
        result.Items[2].AuditId.Should().Be(1);
    }

    [Fact]
    public async Task GetAudits_NullCreateTime_Excluded()
    {
        _db.Audits.AddRange(
            new SyncAudit { AuditId = 1, Username = "alice", CreateTime = DateTime.UtcNow },
            new SyncAudit { AuditId = 2, Username = "bob",   CreateTime = null });
        await _db.SaveChangesAsync();

        var result = await _sut.GetAuditsAsync(new AuditFilter(), default);

        result.Items.Should().HaveCount(1);
        result.Items[0].AuditId.Should().Be(1);
    }

    // ── GetAuditByIdAsync ─────────────────────────────────────

    [Fact]
    public async Task GetAuditById_Exists_ReturnsDto()
    {
        _db.Audits.Add(Audit(42, username: "alice", actionName: "DELETE", objectName: "SyncTrigger"));
        await _db.SaveChangesAsync();

        var dto = await _sut.GetAuditByIdAsync(42, default);

        dto.Should().NotBeNull();
        dto!.AuditId.Should().Be(42);
        dto.Username.Should().Be("alice");
        dto.ActionName.Should().Be("DELETE");
        dto.ObjectName.Should().Be("SyncTrigger");
    }

    [Fact]
    public async Task GetAuditById_NotFound_ReturnsNull()
    {
        var dto = await _sut.GetAuditByIdAsync(999, default);
        dto.Should().BeNull();
    }
}
