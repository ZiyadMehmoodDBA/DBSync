// tests/MSOSync.IntegrationTests/Engine/ApplyEngineTests.cs
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Engine;

[Collection("ApplyEngine")]
public sealed class ApplyEngineTests(ApplyEngineFixture fx) : IAsyncLifetime
{
    // Time-based seed ensures unique BatchId values across test runs (avoids LocalDB PK conflicts).
    private static long _batchIdSeed =
        (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 1_000_000) * 1000;

    private long _batchId;
    private long _batchSeq;

    public async Task InitializeAsync()
    {
        await fx.ClearTestOrdersAsync();

        _batchId  = System.Threading.Interlocked.Increment(ref _batchIdSeed);
        _batchSeq = _batchId;   // unique per test run

        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Ensure the source node exists (seeded in fixture, but guard for safety)
        if (!await db.Nodes.AnyAsync(n => n.NodeId == "src"))
        {
            if (!await db.NodeGroups.AnyAsync(g => g.GroupId == "apply-g"))
                db.NodeGroups.Add(new SyncNodeGroup { GroupId = "apply-g", GroupName = "Apply Test Group" });
            db.Nodes.Add(new SyncNode
            {
                NodeId  = "src",
                GroupId = "apply-g",
                SyncUrl = "http://src",
                Status  = "APPROVED",
            });
        }

        // Seed the incoming batch for this test (BatchId must be set explicitly — ValueGeneratedNever)
        db.IncomingBatches.Add(new SyncIncomingBatch
        {
            BatchId       = _batchId,
            NodeId        = "local",
            ChannelId     = "default",
            SourceNodeId  = "src",
            BatchSequence = _batchSeq,
            ReceivedTime  = DateTime.UtcNow,
            RowCount      = 1,
            Status        = IncomingBatchStatus.New,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private SyncIncomingBatch MakeIncoming() => new()
    {
        BatchId       = _batchId,
        NodeId        = "local",
        ChannelId     = "default",
        SourceNodeId  = "src",
        BatchSequence = _batchSeq,
        ReceivedTime  = DateTime.UtcNow,
        RowCount      = 1,
        Status        = IncomingBatchStatus.New,
    };

    private static BatchPayload MakeBatch(IReadOnlyList<EventPayload> events) =>
        new(1, 1, "default", "src", "local", events.Count, events);

    private static EventPayload InsertEvent(int orderId, string status = "open") =>
        new(1, "t-orders", "INSERT", "dbo.test_orders", null, null,
            $$"""{"order_id":{{orderId}},"status":"{{status}}"}""");

    private static EventPayload UpdateEvent(int orderId, string newStatus) =>
        new(2, "t-orders", "UPDATE", "dbo.test_orders", null,
            $$"""{"order_id":{{orderId}}}""",
            $$"""{"order_id":{{orderId}},"status":"{{newStatus}}"}""");

    private static EventPayload DeleteEvent(int orderId) =>
        new(3, "t-orders", "DELETE", "dbo.test_orders", null,
            $$"""{"order_id":{{orderId}}}""", null);

    private async Task<bool> RowExistsAsync(int orderId)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM [dbo].[test_orders] WHERE [order_id]=@id";
        cmd.Parameters.AddWithValue("@id", orderId);
        return (int)(await cmd.ExecuteScalarAsync())! > 0;
    }

    private async Task<string?> GetStatusStringAsync(int orderId)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT [status] FROM [dbo].[test_orders] WHERE [order_id]=@id";
        cmd.Parameters.AddWithValue("@id", orderId);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertEvent_AppliesRow()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch([InsertEvent(100)]));

        result.Success.Should().BeTrue();
        result.AppliedRows.Should().Be(1);
        result.ErrorRows.Should().Be(0);
        (await RowExistsAsync(100)).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateEvent_ModifiesRow()
    {
        // Pre-insert the row
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO [dbo].[test_orders] VALUES (200, NULL, 'open')";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch([UpdateEvent(200, "closed")]));

        result.Success.Should().BeTrue();
        result.AppliedRows.Should().Be(1);
        (await GetStatusStringAsync(200)).Should().Be("closed");
    }

    [Fact]
    public async Task DeleteEvent_RemovesRow()
    {
        // Pre-insert the row
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO [dbo].[test_orders] VALUES (300, NULL, 'open')";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch([DeleteEvent(300)]));

        result.Success.Should().BeTrue();
        result.AppliedRows.Should().Be(1);
        (await RowExistsAsync(300)).Should().BeFalse();
    }

    [Fact]
    public async Task DuplicateInsert_PartialSuccess()
    {
        // Pre-insert order 400 to create duplicate
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO [dbo].[test_orders] VALUES (400, NULL, 'existing')";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var events = new EventPayload[]
        {
            InsertEvent(401, "ok"),     // succeeds
            InsertEvent(400, "dup"),    // duplicate key — row-level error
            InsertEvent(402, "ok2"),    // succeeds
        };
        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch(events));

        result.Success.Should().BeFalse();
        result.AppliedRows.Should().Be(2);
        result.ErrorRows.Should().Be(1);

        // Batch status should be PartialSuccess
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.IncomingBatches.FindAsync(incoming.BatchId);
        batch!.Status.Should().Be(IncomingBatchStatus.PartialSuccess);
    }

    [Fact]
    public async Task RowNotFound_Update_SavepointContinuesBatch()
    {
        // Pre-insert 501 and 503, but NOT 502
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO [dbo].[test_orders] VALUES (501, NULL, 'start'); " +
                              "INSERT INTO [dbo].[test_orders] VALUES (503, NULL, 'start')";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        var events = new EventPayload[]
        {
            UpdateEvent(501, "done"),    // succeeds
            UpdateEvent(502, "ghost"),   // RowNotFound — row doesn't exist
            UpdateEvent(503, "done"),    // succeeds
        };
        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch(events));

        result.AppliedRows.Should().Be(2);
        result.ErrorRows.Should().Be(1);
        (await GetStatusStringAsync(501)).Should().Be("done");
        (await GetStatusStringAsync(503)).Should().Be("done");
    }

    [Fact]
    public async Task MetadataMissing_TriggerVersionLow_RowError()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Add a v1 trigger (no pk_columns_json)
        if (!await db.Triggers.AnyAsync(t => t.TriggerId == "t-old"))
        {
            db.Triggers.Add(new SyncTrigger
            {
                TriggerId      = "t-old",
                SourceTable    = "dbo.test_orders",
                ChannelId      = "default",
                TriggerVersion = 1,
                PkColumnsJson  = null,
            });
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();
        var evt = new EventPayload(99, "t-old", "INSERT", "dbo.test_orders", null, null,
            """{"order_id":999,"status":"x"}""");

        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch([evt]));

        result.Success.Should().BeFalse();
        result.ErrorRows.Should().Be(1);
        result.AppliedRows.Should().Be(0);
    }

    [Fact]
    public async Task MetadataMissing_UnknownTriggerId_RowError()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        // Use a trigger ID that doesn't exist in the DB
        var evt = new EventPayload(99, "t-ghost", "INSERT", "dbo.test_orders", null, null,
            """{"order_id":998,"status":"x"}""");

        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch([evt]));

        result.Success.Should().BeFalse();
        result.ErrorRows.Should().Be(1);
        result.AppliedRows.Should().Be(0);
    }

    [Fact]
    public async Task MultiTrigger_BatchPreloadsMetadataOnce()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Seed second trigger
        if (!await db.Triggers.AnyAsync(t => t.TriggerId == "t-orders2"))
        {
            db.Triggers.Add(new SyncTrigger
            {
                TriggerId      = "t-orders2",
                SourceTable    = "dbo.test_orders",
                ChannelId      = "default",
                TriggerVersion = 2,
                PkColumnsJson  = """["order_id"]""",
            });
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();
        var events = new EventPayload[]
        {
            new(10, "t-orders",  "INSERT", "dbo.test_orders", null, null, """{"order_id":701,"status":"a"}"""),
            new(11, "t-orders2", "INSERT", "dbo.test_orders", null, null, """{"order_id":702,"status":"b"}"""),
            new(12, "t-orders",  "INSERT", "dbo.test_orders", null, null, """{"order_id":703,"status":"c"}"""),
        };
        var incoming = MakeIncoming();
        var result   = await svc.ApplyAsync(incoming, MakeBatch(events));

        result.AppliedRows.Should().Be(3);
        result.ErrorRows.Should().Be(0);
        (await RowExistsAsync(701)).Should().BeTrue();
        (await RowExistsAsync(702)).Should().BeTrue();
        (await RowExistsAsync(703)).Should().BeTrue();
    }

    [Fact]
    public async Task ReplayBatch_DuplicateInsert_SecondRunPartialSuccess()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();
        var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var events = new EventPayload[] { InsertEvent(600, "first") };

        // First run succeeds
        var incoming1 = MakeIncoming();
        var result1   = await svc.ApplyAsync(incoming1, MakeBatch(events));
        result1.Success.Should().BeTrue();

        // Seed a new incoming batch for the replay (unique BatchId + seq)
        var batchId2  = System.Threading.Interlocked.Increment(ref _batchIdSeed);
        var incoming2 = new SyncIncomingBatch
        {
            BatchId       = batchId2,
            NodeId        = "local",
            ChannelId     = "default",
            SourceNodeId  = "src",
            BatchSequence = batchId2,
            ReceivedTime  = DateTime.UtcNow,
            RowCount      = 1,
            Status        = IncomingBatchStatus.New,
        };
        db.IncomingBatches.Add(incoming2);
        await db.SaveChangesAsync();

        // Second run — same events → DuplicateKey
        var result2 = await svc.ApplyAsync(incoming2, MakeBatch(events));
        result2.Success.Should().BeFalse();
        result2.ErrorRows.Should().Be(1);
        result2.AppliedRows.Should().Be(0);
    }

    [Fact]
    public async Task CancellationToken_CancelsApply()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();   // pre-cancelled token

        var incoming = MakeIncoming();
        var act      = () => svc.ApplyAsync(incoming, MakeBatch([InsertEvent(800)]), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FatalError_RollsBackBatch()
    {
        // Pre-insert order 900 so that a duplicate-key SqlException is triggered for the
        // first event, which causes the (real) classifier to be invoked.  We replace the
        // classifier with a mock that throws InvalidOperationException, so the exception
        // escapes ApplyEventAsync entirely and is caught by the outer catch in ApplyAsync
        // (fatalError = true, transaction rolled back).
        await using var setupConn = new SqlConnection(fx.ConnectionString);
        await setupConn.OpenAsync();
        await using (var cmd = setupConn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO [dbo].[test_orders] VALUES (900, NULL, 'existing')";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = fx.Services.CreateAsyncScope();
        var db          = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connFactory = scope.ServiceProvider.GetRequiredService<ISqlConnectionFactory>();
        var applicator  = scope.ServiceProvider.GetRequiredService<ISqlEventApplicator>();
        var metadata    = scope.ServiceProvider.GetRequiredService<ITriggerApplyMetadataService>();
        var clock       = scope.ServiceProvider.GetRequiredService<IClock>();
        var logger      = scope.ServiceProvider.GetRequiredService<ILogger<ApplyEngine>>();

        // Mock classifier throws on Classify — causing the SqlException handler inside
        // ApplyEventAsync to itself throw, which escapes to the outer fatal-error catch.
        var mockClassifier = new Mock<IApplyFailureClassifier>();
        mockClassifier
            .Setup(c => c.Classify(It.IsAny<int>()))
            .Throws(new InvalidOperationException("simulated fatal classifier error"));

        var engine = new ApplyEngine(db, connFactory, applicator, mockClassifier.Object, metadata, clock, logger);

        // Seed an additional incoming batch row for this test
        var fatalBatchId = System.Threading.Interlocked.Increment(ref _batchIdSeed);
        var fatalIncoming = new SyncIncomingBatch
        {
            BatchId       = fatalBatchId,
            NodeId        = "local",
            ChannelId     = "default",
            SourceNodeId  = "src",
            BatchSequence = fatalBatchId,
            ReceivedTime  = DateTime.UtcNow,
            RowCount      = 2,
            Status        = IncomingBatchStatus.New,
        };
        db.IncomingBatches.Add(fatalIncoming);
        await db.SaveChangesAsync();

        // Two events: first triggers duplicate key SqlException → classifier mock throws → fatal
        var events = new EventPayload[]
        {
            InsertEvent(900, "dup"),    // duplicate key → SqlException → classifier throws → fatal
            InsertEvent(901, "ok"),
        };
        var result = await engine.ApplyAsync(fatalIncoming, MakeBatch(events));

        result.Success.Should().BeFalse();
        result.AppliedRows.Should().Be(0);

        // Batch status must be Error (fatalError path in ApplyAsync)
        await db.Entry(fatalIncoming).ReloadAsync();
        fatalIncoming.Status.Should().Be(IncomingBatchStatus.Error);

        // Row 901 must NOT exist — transaction was rolled back
        (await RowExistsAsync(901)).Should().BeFalse();
    }

    [Fact]
    public async Task CompositePk_Update_Works()
    {
        await fx.ClearTestCompositeAsync();

        await using var scope = fx.Services.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        // Seed a separate incoming batch for this test
        var batchId = System.Threading.Interlocked.Increment(ref _batchIdSeed);
        var incoming = new SyncIncomingBatch
        {
            BatchId       = batchId,
            NodeId        = "local",
            ChannelId     = "default",
            SourceNodeId  = "src",
            BatchSequence = batchId,
            ReceivedTime  = DateTime.UtcNow,
            RowCount      = 2,
            Status        = IncomingBatchStatus.New,
        };
        db.IncomingBatches.Add(incoming);
        await db.SaveChangesAsync();

        var insertEvt = new EventPayload(
            1, "trig-composite", "INSERT", "dbo.test_composite", null, null,
            """{"tenant_id":1,"order_id":100,"status":"new"}""");

        var updateEvt = new EventPayload(
            2, "trig-composite", "UPDATE", "dbo.test_composite", null,
            """{"tenant_id":1,"order_id":100}""",
            """{"tenant_id":1,"order_id":100,"status":"updated"}""");

        var batch  = new BatchPayload(1, 1, "default", "src", "local", 2, [insertEvt, updateEvt]);
        var result = await svc.ApplyAsync(incoming, batch);

        result.Success.Should().BeTrue();
        result.AppliedRows.Should().Be(2);
        result.ErrorRows.Should().Be(0);

        // Verify row has the updated status
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT [status] FROM [dbo].[test_composite] WHERE [tenant_id]=1 AND [order_id]=100";
        var status = (string?)await cmd.ExecuteScalarAsync();
        status.Should().Be("updated");
    }

    [Fact]
    public async Task LargeBatch_Applies()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IApplyService>();

        const int count    = 500;
        const int startId  = 5001;

        // Seed an incoming batch for this test
        var batchId = System.Threading.Interlocked.Increment(ref _batchIdSeed);
        var incoming = new SyncIncomingBatch
        {
            BatchId       = batchId,
            NodeId        = "local",
            ChannelId     = "default",
            SourceNodeId  = "src",
            BatchSequence = batchId,
            ReceivedTime  = DateTime.UtcNow,
            RowCount      = count,
            Status        = IncomingBatchStatus.New,
        };
        db.IncomingBatches.Add(incoming);
        await db.SaveChangesAsync();

        var events = Enumerable.Range(startId, count)
            .Select(id => new EventPayload(
                id, "t-orders", "INSERT", "dbo.test_orders", null, null,
                $$"""{"order_id":{{id}},"status":"bulk"}"""))
            .ToList();

        var batch  = new BatchPayload(1, 1, "default", "src", "local", count, events);
        var result = await svc.ApplyAsync(incoming, batch);

        result.AppliedRows.Should().Be(count);
        result.ErrorRows.Should().Be(0);
        result.Success.Should().BeTrue();

        // Verify row count in DB for this range
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(1) FROM [dbo].[test_orders] WHERE [order_id] BETWEEN {startId} AND {startId + count - 1}";
        var rowCount = (int)(await cmd.ExecuteScalarAsync())!;
        rowCount.Should().Be(count);
    }
}
