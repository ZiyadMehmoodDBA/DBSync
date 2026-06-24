using FluentAssertions;
using MSOSync.Metadata.Events;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Events;

public sealed class EventQueryServiceTests
{
    private static (EventQueryService Svc, AppDbContext Db) Make()
    {
        var db  = TestDbContext.Create();
        var svc = new EventQueryService(db);
        return (svc, db);
    }

    private static SyncDataEvent MakeEvent(string nodeId, string triggerId, char eventType,
        bool isProcessed = false) => new()
    {
        TriggerId    = triggerId,
        SourceNodeId = nodeId,
        ChannelId    = "ch-1",
        EventType    = eventType,
        TableName    = "dbo.Product",
        CreateTime   = DateTime.UtcNow
    };

    [Fact]
    public async Task GetEventsAsync_NoFilter_ReturnsAll()
    {
        var (svc, db) = Make();
        db.DataEvents.AddRange(
            MakeEvent("node-1", "trig-a", 'I'),
            MakeEvent("node-2", "trig-b", 'U'));
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter(), default);

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetEventsAsync_FilterBySourceNode_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.DataEvents.AddRange(
            MakeEvent("node-1", "trig-a", 'I'),
            MakeEvent("node-2", "trig-b", 'U'));
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter { SourceNodeId = "node-1" }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().SourceNodeId.Should().Be("node-1");
    }

    [Fact]
    public async Task GetEventsAsync_FilterByEventType_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.DataEvents.AddRange(
            MakeEvent("node-1", "trig-a", 'I'),
            MakeEvent("node-1", "trig-b", 'U'),
            MakeEvent("node-1", "trig-c", 'D'));
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter { EventType = 'U' }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().EventType.Should().Be('U');
    }

    [Fact]
    public async Task GetEventsAsync_FilterByIsProcessed_ReturnsMatching()
    {
        var (svc, db) = Make();
        var e1 = MakeEvent("node-1", "trig-a", 'I');
        var e2 = MakeEvent("node-1", "trig-b", 'U');
        e2.IsProcessed = true;
        db.DataEvents.AddRange(e1, e2);
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter { IsProcessed = true }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().IsProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task GetEventsAsync_FilterByDateRange_ReturnsMatching()
    {
        var (svc, db) = Make();
        var old = MakeEvent("node-1", "trig-a", 'I');
        old.CreateTime = DateTime.UtcNow.AddDays(-10);
        var recent = MakeEvent("node-1", "trig-b", 'U');
        recent.CreateTime = DateTime.UtcNow;
        db.DataEvents.AddRange(old, recent);
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(
            new EventFilter { From = DateTime.UtcNow.AddDays(-1) }, default);

        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetEventsAsync_Pagination_HonorsPageSize()
    {
        var (svc, db) = Make();
        for (int i = 0; i < 10; i++)
            db.DataEvents.Add(MakeEvent("node-1", $"trig-{i}", 'I'));
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter { Page = 1, PageSize = 3 }, default);

        result.TotalCount.Should().Be(10);
        result.Items.Should().HaveCount(3);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task GetEventsAsync_EventWithBatch_ReturnsBatchId()
    {
        var (svc, db) = Make();
        var ev = MakeEvent("node-1", "trig-a", 'I');
        db.DataEvents.Add(ev);
        await db.SaveChangesAsync();

        db.DataEventBatches.Add(new SyncDataEventBatch { EventId = ev.EventId, BatchId = 999L });
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter(), default);

        result.Items.Single().BatchId.Should().Be(999L);
    }

    [Fact]
    public async Task GetEventsAsync_EventWithoutBatch_BatchIdNull()
    {
        var (svc, db) = Make();
        db.DataEvents.Add(MakeEvent("node-1", "trig-a", 'I'));
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter(), default);

        result.Items.Single().BatchId.Should().BeNull();
    }

    [Fact]
    public async Task GetEventByIdAsync_Exists_ReturnsDetailDto()
    {
        var (svc, db) = Make();
        var ev = MakeEvent("node-1", "trig-a", 'I');
        ev.PkData = "{\"id\":1}";
        db.DataEvents.Add(ev);
        await db.SaveChangesAsync();

        var dto = await svc.GetEventByIdAsync(ev.EventId, default);

        dto.Should().NotBeNull();
        dto!.EventId.Should().Be(ev.EventId);
        dto.PkData.Should().Be("{\"id\":1}");
    }

    [Fact]
    public async Task GetEventByIdAsync_Missing_ReturnsNull()
    {
        var (svc, db) = Make();
        var dto = await svc.GetEventByIdAsync(99999L, default);
        dto.Should().BeNull();
    }
}
