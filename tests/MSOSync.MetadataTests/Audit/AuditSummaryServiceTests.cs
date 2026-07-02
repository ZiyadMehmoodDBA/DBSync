using FluentAssertions;
using MSOSync.Metadata.Audit;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Audit;

public sealed class AuditSummaryServiceTests : IDisposable
{
    private readonly AppDbContext        _db;
    private readonly AuditSummaryService _sut;
    private static readonly DateTime BaseTime = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    public AuditSummaryServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new AuditSummaryService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static SyncAudit Audit(long id, string actionName = "LOGIN_SUCCESS",
        string? username = "alice", string? objectName = null,
        DateTime? createTime = null) => new()
    {
        AuditId    = id,
        ActionName = actionName,
        Username   = username,
        ObjectName = objectName,
        CreateTime = createTime ?? BaseTime,
    };

    [Fact]
    public async Task GetSummary_CountsTotalActions()
    {
        _db.Audits.AddRange(Audit(1), Audit(2), Audit(3));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.TotalActions.Should().Be(3);
    }

    [Fact]
    public async Task GetSummary_CountsFailedOperations()
    {
        _db.Audits.AddRange(
            Audit(1, actionName: "LOGIN_SUCCESS"),
            Audit(2, actionName: "LOGIN_FAILURE"),
            Audit(3, actionName: "ACCOUNT_LOCKED"),
            Audit(4, actionName: "TOKEN_REUSE_DETECTED"));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.FailedOperations.Should().Be(3); // FAILURE, LOCKED, REUSE
    }

    [Fact]
    public async Task GetSummary_CountsPermissionChanges()
    {
        _db.Audits.AddRange(
            Audit(1, actionName: "PERMISSION_GRANTED"),
            Audit(2, actionName: "ROLE_UPDATED"),
            Audit(3, actionName: "LOGIN_SUCCESS"));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.PermissionChanges.Should().Be(2);
    }

    [Fact]
    public async Task GetSummary_ByDay_IncludesZeroCountDays()
    {
        // Only one audit on day 1; day 2 has no audits
        var day1 = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc);
        _db.Audits.Add(Audit(1, createTime: day1));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(day1.AddHours(-1), day2.AddHours(1));

        result.ByDay.Should().HaveCount(2);
        result.ByDay.First(d => d.Date == DateOnly.FromDateTime(day1)).Total.Should().Be(1);
        result.ByDay.First(d => d.Date == DateOnly.FromDateTime(day2)).Total.Should().Be(0);
    }

    [Fact]
    public async Task GetSummary_ByDay_TracksDailyFailures()
    {
        _db.Audits.AddRange(
            Audit(1, actionName: "LOGIN_SUCCESS", createTime: BaseTime),
            Audit(2, actionName: "LOGIN_FAILURE", createTime: BaseTime));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        var bucket = result.ByDay.Single(d => d.Date == DateOnly.FromDateTime(BaseTime));
        bucket.Total.Should().Be(2);
        bucket.Failed.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_ByUser_TopTenDescending()
    {
        for (int i = 1; i <= 12; i++)
            _db.Audits.Add(Audit(i, username: $"user{i:D2}", createTime: BaseTime));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.ByUser.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetSummary_ByEntityType_GroupsByObjectName()
    {
        _db.Audits.AddRange(
            Audit(1, objectName: "SyncNode",    createTime: BaseTime),
            Audit(2, objectName: "SyncNode",    createTime: BaseTime),
            Audit(3, objectName: "SyncTrigger", createTime: BaseTime));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.ByEntityType.Should().HaveCount(2);
        result.ByEntityType.First().Count.Should().Be(2);
    }

    [Fact]
    public async Task GetSummary_ExcludesAuditsOutsideDateRange()
    {
        _db.Audits.AddRange(
            Audit(1, createTime: BaseTime.AddDays(-2)),   // before range
            Audit(2, createTime: BaseTime),                // in range
            Audit(3, createTime: BaseTime.AddDays(2)));   // after range
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(
            BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.TotalActions.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_TopParameters_LimitsTenByParameterAction()
    {
        for (int i = 1; i <= 15; i++)
            _db.Audits.Add(Audit(i, actionName: "PARAMETER_UPDATED",
                objectName: $"param{i:D2}", createTime: BaseTime));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.TopParameters.Should().HaveCount(10);
    }
}
