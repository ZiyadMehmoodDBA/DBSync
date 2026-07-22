using FluentAssertions;
using MSOSync.Metadata.Operations.Timeline;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Operations.Timeline;

public sealed class OperationTimelineServiceTests : IDisposable
{
    private readonly global::MSOSync.Persistence.AppDbContext _db = TestDbContext.Create();

    public void Dispose() => _db.Dispose();

    private async Task<Guid> SeedOpAsync(
        string type, string status,
        DateTime startedAt, DateTime? completedAt = null)
    {
        var id = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId   = id,
            OperationType = type,
            Status        = status,
            Source        = "User",
            StartedAt     = startedAt,
            CompletedAt   = completedAt,
            CanCancel     = false,
            CanRetry      = false,
            TenantId      = Guid.Empty,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task GetTimelineAsync_returns_operations_in_range()
    {
        var from = DateTime.UtcNow.AddHours(-2);
        var to   = DateTime.UtcNow;
        await SeedOpAsync("Export", "Completed", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(-30));
        await SeedOpAsync("Export", "Completed", DateTime.UtcNow.AddHours(-5)); // outside range

        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(from, to, null, 200);

        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetTimelineAsync_filters_by_type()
    {
        var from = DateTime.UtcNow.AddHours(-2);
        var to   = DateTime.UtcNow;
        await SeedOpAsync("Export",     "Completed", DateTime.UtcNow.AddHours(-1));
        await SeedOpAsync("BatchReplay","Running",   DateTime.UtcNow.AddMinutes(-30));

        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(from, to, ["Export"], 200);

        result.Items.Should().HaveCount(1);
        result.Items[0].Type.Should().Be("Export");
    }

    [Fact]
    public async Task GetTimelineAsync_HasMore_true_when_exceeds_limit()
    {
        var from = DateTime.UtcNow.AddHours(-2);
        var to   = DateTime.UtcNow;
        for (var i = 0; i < 6; i++)
            await SeedOpAsync("Export", "Completed", DateTime.UtcNow.AddMinutes(-i - 1));

        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(from, to, null, 5);

        result.HasMore.Should().BeTrue();
        result.Items.Should().HaveCount(5);
        result.ReturnedCount.Should().Be(5);
    }

    [Fact]
    public async Task GetTimelineAsync_orders_by_startedAt_then_operationId()
    {
        var from = DateTime.UtcNow.AddHours(-2);
        var to   = DateTime.UtcNow;
        var t = DateTime.UtcNow.AddHours(-1);
        var id1 = await SeedOpAsync("Export", "Completed", t);
        var id2 = await SeedOpAsync("Export", "Completed", t); // same time, sort by id

        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(from, to, null, 200);

        var ids = result.Items.Select(i => i.OperationId).ToList();
        ids.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetTimelineAsync_label_uses_progressMessage_then_summary_then_type()
    {
        var from = DateTime.UtcNow.AddHours(-2);
        var to   = DateTime.UtcNow;
        var id = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = id, OperationType = "Export", Status = "Running",
            Source = "Worker", StartedAt = DateTime.UtcNow.AddMinutes(-10),
            ProgressMessage = "Processing batch 3 of 10",
            Summary = "Export job", CanCancel = false, CanRetry = false, TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(from, to, null, 200);

        result.Items[0].Label.Should().Be("Processing batch 3 of 10");
    }

    [Fact]
    public async Task GetTimelineAsync_empty_db_returns_empty_result()
    {
        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, 200);
        result.Items.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }
}
