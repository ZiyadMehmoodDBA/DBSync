using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Batch;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using MSOSync.Trigger;
using Xunit;

namespace MSOSync.IntegrationTests.Engine;

[Collection("Engine")]
public sealed class EngineTests(EngineFixture fixture)
{
    private IServiceScope Scope() => fixture.Services.CreateScope();

    // ── Trigger Installation ────────────────────────────────────────────────

    [Fact]
    public async Task TriggerInstall_CreatesRealTriggerInDatabase()
    {
        using var scope = Scope();
        var svc = scope.ServiceProvider.GetRequiredService<ITriggerInstallationService>();
        var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await svc.RebuildAsync(EngineFixture.TriggerId);

        var count = await db.Database
            .SqlQuery<int>($"""
                SELECT COUNT(1) AS Value
                FROM sys.triggers t
                WHERE t.name = N'msosync__{EngineFixture.TriggerId}'
                """)
            .SingleAsync();

        count.Should().Be(1);
    }

    [Fact]
    public async Task TriggerDrift_AfterInstall_ReturnsNoDrift()
    {
        using var scope = Scope();
        var svc      = scope.ServiceProvider.GetRequiredService<ITriggerInstallationService>();
        var detector = scope.ServiceProvider.GetRequiredService<ITriggerDriftDetector>();

        await svc.RebuildAsync(EngineFixture.TriggerId);
        var result = await detector.VerifyAsync(EngineFixture.TriggerId);

        result.Status.Should().Be(TriggerDriftStatus.Valid);
    }

    [Fact]
    public async Task TriggerDrift_AfterManualAlteration_DetectsDrift()
    {
        using var scope = Scope();
        var svc      = scope.ServiceProvider.GetRequiredService<ITriggerInstallationService>();
        var detector = scope.ServiceProvider.GetRequiredService<ITriggerDriftDetector>();
        var db       = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await svc.RebuildAsync(EngineFixture.TriggerId);

        // Alter the trigger body manually to simulate out-of-band change
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE OR ALTER TRIGGER [msosync__{EngineFixture.TriggerId}]
            ON {EngineFixture.TestTable} AFTER INSERT, UPDATE, DELETE
            AS BEGIN SELECT 1 END
            """);

        var result = await detector.VerifyAsync(EngineFixture.TriggerId);
        result.Status.Should().Be(TriggerDriftStatus.Drift);
    }

    // ── Event Reading ───────────────────────────────────────────────────────

    [Fact]
    public async Task EventReader_NoEvents_ReturnsEmptyList()
    {
        using var scope = Scope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IEventReader>();

        // Ensure no unprocessed events for our trigger
        await db.DataEvents
            .Where(e => e.TriggerId == EngineFixture.TriggerId && !e.IsProcessed)
            .ExecuteDeleteAsync();

        var events = await reader.ReadAsync(100);
        events.Should().NotContain(e => e.TriggerId == EngineFixture.TriggerId);
    }

    [Fact]
    public async Task EventReader_SeedsAndReads_ReturnsUnprocessedEvents()
    {
        using var scope = Scope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IEventReader>();

        var evt = new SyncDataEvent
        {
            TriggerId    = EngineFixture.TriggerId,
            ChannelId    = EngineFixture.ChannelId,
            SourceNodeId = EngineFixture.NodeId,
            EventType    = 'I',
            TableName    = EngineFixture.TestTable,
            CreateTime   = DateTime.UtcNow,
            IsProcessed  = false,
        };
        db.DataEvents.Add(evt);
        await db.SaveChangesAsync();

        try
        {
            var events = await reader.ReadAsync(100);
            events.Should().Contain(e => e.EventId == evt.EventId);
        }
        finally
        {
            await db.DataEvents.Where(e => e.EventId == evt.EventId).ExecuteDeleteAsync();
        }
    }

    // ── Routing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoutingService_Resolve_ReturnsNodeForSeededTrigger()
    {
        using var scope = Scope();
        var routing = scope.ServiceProvider.GetRequiredService<IRoutingService>();

        var nodes = await routing.ResolveAsync(EngineFixture.TriggerId);

        nodes.Should().Contain(EngineFixture.NodeId);
    }

    // ── Batch Creation ──────────────────────────────────────────────────────

    [Fact]
    public async Task BatchCreator_CreatesAndPersistsBatch_ForSeededEvent()
    {
        using var scope = Scope();
        var db      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var creator = scope.ServiceProvider.GetRequiredService<IBatchCreator>();
        var routing = scope.ServiceProvider.GetRequiredService<IRoutingService>();

        var evt = new SyncDataEvent
        {
            TriggerId    = EngineFixture.TriggerId,
            ChannelId    = EngineFixture.ChannelId,
            SourceNodeId = EngineFixture.NodeId,
            EventType    = 'I',
            TableName    = EngineFixture.TestTable,
            CreateTime   = DateTime.UtcNow,
            IsProcessed  = false,
        };
        db.DataEvents.Add(evt);
        await db.SaveChangesAsync();

        try
        {
            var routes = new Dictionary<long, IReadOnlyList<string>>
            {
                [evt.EventId] = await routing.ResolveAsync(EngineFixture.TriggerId),
            };

            var batches = await creator.CreateBatchesAsync([evt], routes);

            batches.Should().HaveCount(1);
            var batch = batches[0];
            batch.NodeId.Should().Be(EngineFixture.NodeId);
            batch.ChannelId.Should().Be(EngineFixture.ChannelId);
            batch.Status.Should().Be((byte)BatchStatus.New);
            batch.RowCount.Should().Be(1);

            // Verify event marked processed
            var refreshed = await db.DataEvents.AsNoTracking()
                .FirstAsync(e => e.EventId == evt.EventId);
            refreshed.IsProcessed.Should().BeTrue();
        }
        finally
        {
            await db.DataEventBatches
                .Where(l => l.EventId == evt.EventId)
                .ExecuteDeleteAsync();
            await db.OutgoingBatches
                .Where(b => b.NodeId == EngineFixture.NodeId && b.ChannelId == EngineFixture.ChannelId)
                .ExecuteDeleteAsync();
            await db.DataEvents
                .Where(e => e.EventId == evt.EventId)
                .ExecuteDeleteAsync();
        }
    }

    // ── Full SyncEngine Cycle ───────────────────────────────────────────────

    [Fact]
    public async Task SyncEngine_NoEvents_CompletesWithoutCreatingBatches()
    {
        using var scope = Scope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();

        await db.DataEvents
            .Where(e => e.TriggerId == EngineFixture.TriggerId && !e.IsProcessed)
            .ExecuteDeleteAsync();

        var batchCountBefore = await db.OutgoingBatches.CountAsync();

        await engine.RunAsync();

        var batchCountAfter = await db.OutgoingBatches.CountAsync();
        batchCountAfter.Should().Be(batchCountBefore);
    }

    [Fact]
    public async Task SyncEngine_WithEvent_CreatesNewBatch_AndMarksEventProcessed()
    {
        using var scope = Scope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();

        var evt = new SyncDataEvent
        {
            TriggerId    = EngineFixture.TriggerId,
            ChannelId    = EngineFixture.ChannelId,
            SourceNodeId = EngineFixture.NodeId,
            EventType    = 'I',
            TableName    = EngineFixture.TestTable,
            CreateTime   = DateTime.UtcNow,
            IsProcessed  = false,
        };
        db.DataEvents.Add(evt);
        await db.SaveChangesAsync();

        try
        {
            await engine.RunAsync();

            var refreshed = await db.DataEvents.AsNoTracking()
                .FirstAsync(e => e.EventId == evt.EventId);
            refreshed.IsProcessed.Should().BeTrue();

            var batchLink = await db.DataEventBatches.AsNoTracking()
                .FirstOrDefaultAsync(l => l.EventId == evt.EventId);
            batchLink.Should().NotBeNull();
        }
        finally
        {
            await db.DataEventBatches
                .Where(l => l.EventId == evt.EventId)
                .ExecuteDeleteAsync();
            await db.DataEvents
                .Where(e => e.EventId == evt.EventId)
                .ExecuteDeleteAsync();
        }
    }
}
