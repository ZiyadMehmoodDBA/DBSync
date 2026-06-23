using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Scheduler;
using MSOSync.Engine;
using MSOSync.Transport;
using MSOSync.Transport.Payloads;
using Xunit;

namespace MSOSync.IntegrationTests.Transport;

[Collection("Transport")]
public sealed class TransportTests(TransportFixture fixture)
{
    private readonly GzipCompressionService _compression = new();

    private static readonly JsonSerializerOptions JsonOpts =
        new(TransportJsonContext.Default.Options);

    // Client that authenticates as source-node using the seeded BCrypt hash
    private HttpClient CreateClient()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Node-Id",    TransportFixture.SourceNodeId);
        client.DefaultRequestHeaders.Add("X-Node-Token", TransportFixture.TestToken);
        return client;
    }

    private AppDbContext CreateDbContext() => fixture.CreateDbContext();

    // ── Test 1: Duplicate push is ignored ────────────────────────────────────

    [Fact]
    public async Task DuplicatePush_SecondCallIgnored_OneIncomingBatchRow()
    {
        // Ensure "default" channel exists (seeded in fixture, but gap-ch isn't)
        var payload = new BatchPayload(
            BatchId:       2001,
            BatchSequence: 1,
            ChannelId:     "default",
            SourceNodeId:  TransportFixture.SourceNodeId,
            TargetNodeId:  TransportFixture.LocalNodeId,
            RowCount:      3,
            Events:        [new EventPayload(1, "trig1", "INSERT", "dbo.orders", null, null, null)]);

        using var client = CreateClient();

        // First push
        var r1 = await PushBatch(client, payload);
        r1.StatusCode.Should().Be(HttpStatusCode.OK, $"first push: {await r1.Content.ReadAsStringAsync()}");

        // Second push (duplicate)
        var r2 = await PushBatch(client, payload);
        r2.StatusCode.Should().Be(HttpStatusCode.OK, $"second push: {await r2.Content.ReadAsStringAsync()}");

        // Only one IncomingBatch row
        await using var db = CreateDbContext();
        var count = await db.IncomingBatches.CountAsync(
            b => b.BatchSequence == 1 && b.SourceNodeId == TransportFixture.SourceNodeId);
        count.Should().Be(1);
    }

    // ── Test 2: Duplicate ACK is ignored ─────────────────────────────────────

    [Fact]
    public async Task DuplicateAck_ThreeCalls_BatchStaysAcknowledged()
    {
        // Seed an outgoing batch
        await using var db = CreateDbContext();
        var batch = new SyncOutgoingBatch
        {
            BatchSequence = 10,
            NodeId        = TransportFixture.LocalNodeId,
            ChannelId     = "default",
            Status        = (byte)BatchStatus.New,
            RowCount      = 1,
        };
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        var ack = new AckPayload(batch.BatchId, 10, TransportFixture.LocalNodeId, true, null, DateTimeOffset.UtcNow);

        using var client = CreateClient();

        // Three ACK calls
        for (var i = 0; i < 3; i++)
        {
            var r = await client.PostAsJsonAsync("/api/v1/sync/ack", ack);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"call {i + 1}: {await r.Content.ReadAsStringAsync()}");
        }

        await using var db2 = CreateDbContext();
        var updated = await db2.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Acknowledged);
    }

    // ── Test 3: Pull with no batches returns 204 ─────────────────────────────

    [Fact]
    public async Task Pull_NoBatch_Returns204()
    {
        // Pull endpoint: req.TargetNodeId must equal the authenticated caller's nodeId ("source-node")
        var req    = new PullRequest(TransportFixture.SourceNodeId, "empty-channel", 0);
        using var client = CreateClient();
        var r      = await client.PostAsJsonAsync("/api/v1/sync/pull", req);
        r.StatusCode.Should().Be(HttpStatusCode.NoContent, await r.Content.ReadAsStringAsync());
    }

    // ── Test 4: Sequence gap returns 409 ─────────────────────────────────────

    [Fact]
    public async Task SequenceGap_Returns409_WithCode()
    {
        const string gapSource  = "gap-source";
        const string gapChannel = "gap-ch";

        await using var db = CreateDbContext();

        // Seed: gap-ch channel
        if (!await db.Channels.AnyAsync(c => c.ChannelId == gapChannel))
        {
            db.Channels.Add(new SyncChannel
            {
                ChannelId      = gapChannel,
                Priority       = 1,
                BatchSize      = 1000,
                MaxBatchToSend = 10,
                MaxDataSize    = 1048576L,
            });
            await db.SaveChangesAsync();
        }

        // Seed: gap-source node
        if (!await db.Nodes.AnyAsync(n => n.NodeId == gapSource))
        {
            db.Nodes.Add(new SyncNode
            {
                NodeId        = gapSource,
                GroupId       = TransportFixture.GroupId,
                SyncUrl       = "http://gap-source",
                Status        = "APPROVED",
                SyncEnabled   = true,
                TransportMode = TransportMode.Pull,
            });
            await db.SaveChangesAsync();
        }

        // Seed: NodeSecurity for gap-source so it can authenticate
        if (!await db.NodeSecurities.AnyAsync(s => s.NodeId == gapSource))
        {
            db.NodeSecurities.Add(new SyncNodeSecurity
            {
                NodeId           = gapSource,
                CurrentTokenHash = BCrypt.Net.BCrypt.HashPassword(TransportFixture.TestToken, workFactor: 4),
                CreatedTime      = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // Two existing incoming batches at seq 1 and 2
        if (!await db.IncomingBatches.AnyAsync(b => b.BatchId == 3001))
        {
            db.IncomingBatches.Add(new SyncIncomingBatch
            {
                BatchId       = 3001,
                NodeId        = TransportFixture.LocalNodeId,
                ChannelId     = gapChannel,
                SourceNodeId  = gapSource,
                BatchSequence = 1,
                ReceivedTime  = DateTime.UtcNow,
                RowCount      = 1,
            });
        }
        if (!await db.IncomingBatches.AnyAsync(b => b.BatchId == 3002))
        {
            db.IncomingBatches.Add(new SyncIncomingBatch
            {
                BatchId       = 3002,
                NodeId        = TransportFixture.LocalNodeId,
                ChannelId     = gapChannel,
                SourceNodeId  = gapSource,
                BatchSequence = 2,
                ReceivedTime  = DateTime.UtcNow,
                RowCount      = 1,
            });
        }
        await db.SaveChangesAsync();

        // Push batch with seq=4 (gap: seq=3 missing)
        var payload = new BatchPayload(
            3004, 4, gapChannel, gapSource, TransportFixture.LocalNodeId, 1,
            [new EventPayload(100, "t", "INSERT", "t", null, null, null)]);

        // Use gap-source credentials
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Node-Id",    gapSource);
        client.DefaultRequestHeaders.Add("X-Node-Token", TransportFixture.TestToken);

        var r = await PushBatch(client, payload);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
        var body = await r.Content.ReadAsStringAsync();
        body.Should().Contain("SEQUENCE_GAP");
    }

    // ── Test 5: SchedulerRecovery moves stale Sending → Error ────────────────

    [Fact]
    public async Task SchedulerRecovery_StaleSending_MovesToError()
    {
        await using var db = CreateDbContext();

        var staleBatch = new SyncOutgoingBatch
        {
            BatchSequence = 20,
            NodeId        = TransportFixture.LocalNodeId,
            ChannelId     = "default",
            Status        = (byte)BatchStatus.Sending,
            SentTime      = DateTime.UtcNow.AddMinutes(-15),  // 15 min ago — stale
        };
        db.OutgoingBatches.Add(staleBatch);
        await db.SaveChangesAsync();

        // Instantiate SchedulerRecovery directly using services from the test host
        using var scope = fixture.Services.CreateScope();
        var sp          = scope.ServiceProvider;
        var recovery    = new SchedulerRecovery(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<SchedulerRecovery>>());

        await recovery.StartAsync(default);

        await using var db2 = CreateDbContext();
        var updated = await db2.OutgoingBatches.FindAsync(staleBatch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Error);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PushBatch(HttpClient client, BatchPayload payload)
    {
        var json    = JsonSerializer.Serialize(payload, JsonOpts);
        var bytes   = Encoding.UTF8.GetBytes(json);
        var gzipped = _compression.Compress(bytes);
        var content = new ByteArrayContent(gzipped);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");
        return await client.PostAsync("/api/v1/sync/push", content);
    }
}
