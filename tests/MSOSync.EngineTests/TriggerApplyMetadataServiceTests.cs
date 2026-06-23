// tests/MSOSync.EngineTests/TriggerApplyMetadataServiceTests.cs
using FluentAssertions;
using MSOSync.Engine;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class TriggerApplyMetadataServiceTests
{
    private static TriggerApplyMetadataService CreateService(out AppDbContext db)
    {
        db = TestDbContext.Create();
        return new TriggerApplyMetadataService(db);
    }

    [Fact]
    public async Task GetMetadataAsync_KnownTrigger_ReturnsMapped()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId      = "t1",
            SourceTable    = "dbo.orders",
            ChannelId      = "ch",
            TriggerVersion = 2,
            PkColumnsJson  = """["order_id"]"""
        });
        await db.SaveChangesAsync();

        var result = await svc.GetMetadataAsync(["t1"]);

        result.Should().ContainKey("t1");
        var meta = result["t1"];
        meta.SchemaName.Should().Be("dbo");
        meta.TableName.Should().Be("orders");
        meta.PkColumns.Should().Equal("order_id");
        meta.TriggerVersion.Should().Be(2);
    }

    [Fact]
    public async Task GetMetadataAsync_UnknownTrigger_ReturnsEmpty()
    {
        var svc = CreateService(out _);
        var result = await svc.GetMetadataAsync(["does-not-exist"]);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMetadataAsync_NullPkColumnsJson_ReturnsEmptyPkColumns()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId      = "t2",
            SourceTable    = "sales.products",
            ChannelId      = "ch",
            TriggerVersion = 1,
            PkColumnsJson  = null
        });
        await db.SaveChangesAsync();

        var result = await svc.GetMetadataAsync(["t2"]);
        result["t2"].PkColumns.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMetadataAsync_MalformedPkColumnsJson_Throws()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId      = "t3",
            SourceTable    = "dbo.bad",
            ChannelId      = "ch",
            TriggerVersion = 2,
            PkColumnsJson  = "{bad json"
        });
        await db.SaveChangesAsync();

        var act = async () => await svc.GetMetadataAsync(["t3"]);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetMetadataAsync_CompositePk_ReturnsBothColumns()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId      = "t4",
            SourceTable    = "dbo.items",
            ChannelId      = "ch",
            TriggerVersion = 2,
            PkColumnsJson  = """["tenant_id","item_id"]"""
        });
        await db.SaveChangesAsync();

        var result = await svc.GetMetadataAsync(["t4"]);
        result["t4"].PkColumns.Should().Equal("tenant_id", "item_id");
    }

    [Fact]
    public async Task GetMetadataAsync_OnlyFetchesRequestedIds()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger { TriggerId = "a", SourceTable = "dbo.a", ChannelId = "ch", TriggerVersion = 2, PkColumnsJson = """["id"]""" });
        db.Triggers.Add(new SyncTrigger { TriggerId = "b", SourceTable = "dbo.b", ChannelId = "ch", TriggerVersion = 2, PkColumnsJson = """["id"]""" });
        await db.SaveChangesAsync();

        var result = await svc.GetMetadataAsync(["a"]);
        result.Should().ContainKey("a").And.NotContainKey("b");
    }
}
