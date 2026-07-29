using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSOSync.Api.Health;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Health;

public sealed class SloServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IOptions<SloOptions> _opts =
        Options.Create(new SloOptions { DeliveryRateTarget = 0.999, LatencyP99TargetMs = 5000, WindowHours = 24 });

    public SloServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetStatusAsync_Returns100PctDelivery_WhenAllBatchesSucceed()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < 10; i++)
        {
            _db.OutgoingBatches.Add(new SyncOutgoingBatch
            {
                BatchId       = i + 1,
                BatchSequence = i + 1,
                NodeId        = "node-1",
                ChannelId     = "ch1",
                Status        = (byte)BatchStatus.Acknowledged,
                CreateTime    = now.AddHours(-1),
                AckTime       = now.AddHours(-1).AddSeconds(100 + i * 10),
            });
        }
        await _db.SaveChangesAsync();

        var svc = new SloService(_db, _opts);
        var status = await svc.GetStatusAsync();

        status.DeliveryRate.Should().Be(1.0);
        status.DeliveryRateMet.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsDeliveryRateBelowTarget_WhenBatchesFail()
    {
        var now = DateTime.UtcNow;
        // 2 failed batches (with AckTime so they're counted in the window)
        for (var i = 0; i < 2; i++)
        {
            _db.OutgoingBatches.Add(new SyncOutgoingBatch
            {
                BatchId       = i + 1,
                BatchSequence = i + 1,
                NodeId        = "node-1",
                ChannelId     = "ch1",
                Status        = (byte)BatchStatus.Error,
                CreateTime    = now.AddHours(-1),
                AckTime       = now.AddHours(-1).AddSeconds(200 + i),
            });
        }
        // 998 successful batches → 998/1000 = 0.998 < 0.999 target
        for (var i = 0; i < 998; i++)
        {
            _db.OutgoingBatches.Add(new SyncOutgoingBatch
            {
                BatchId       = i + 3,
                BatchSequence = i + 3,
                NodeId        = "node-1",
                ChannelId     = "ch1",
                Status        = (byte)BatchStatus.Acknowledged,
                CreateTime    = now.AddHours(-1),
                AckTime       = now.AddHours(-1).AddSeconds(100 + i),
            });
        }
        await _db.SaveChangesAsync();

        var svc = new SloService(_db, _opts);
        var status = await svc.GetStatusAsync();

        // 998/1000 = 0.998, which is below the 0.999 target
        status.DeliveryRate.Should().BeApproximately(0.998, 0.0001);
        status.DeliveryRateMet.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsWindowBounds()
    {
        var svc = new SloService(_db, _opts);
        var status = await svc.GetStatusAsync();

        (status.WindowEnd - status.WindowStart).TotalHours.Should().BeApproximately(24, 0.1);
    }

    [Fact]
    public async Task GetStatusAsync_Returns100PctDelivery_WhenNoBatches()
    {
        var svc = new SloService(_db, _opts);
        var status = await svc.GetStatusAsync();

        status.DeliveryRate.Should().Be(1.0);
        status.DeliveryRateMet.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_ExcludesBatchesOutsideWindow()
    {
        var now = DateTime.UtcNow;
        // Batch outside the 24-hour window — should be excluded
        _db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            BatchId       = 1,
            BatchSequence = 1,
            NodeId        = "node-1",
            ChannelId     = "ch1",
            Status        = (byte)BatchStatus.Error,
            CreateTime    = now.AddHours(-48),
            AckTime       = now.AddHours(-47),
        });
        // Batch inside the window
        _db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            BatchId       = 2,
            BatchSequence = 2,
            NodeId        = "node-1",
            ChannelId     = "ch1",
            Status        = (byte)BatchStatus.Acknowledged,
            CreateTime    = now.AddHours(-1),
            AckTime       = now.AddHours(-1).AddSeconds(100),
        });
        await _db.SaveChangesAsync();

        var svc = new SloService(_db, _opts);
        var status = await svc.GetStatusAsync();

        // Only 1 batch in window, it succeeded → 100 %
        status.DeliveryRate.Should().Be(1.0);
    }

    [Fact]
    public async Task GetStatusAsync_ComputesP99Latency()
    {
        var now = DateTime.UtcNow;
        // Seed 100 successful batches with latencies 1000ms..100000ms (1s..100s)
        for (var i = 1; i <= 100; i++)
        {
            _db.OutgoingBatches.Add(new SyncOutgoingBatch
            {
                BatchId       = i,
                BatchSequence = i,
                NodeId        = "node-1",
                ChannelId     = "ch1",
                Status        = (byte)BatchStatus.Acknowledged,
                CreateTime    = now.AddHours(-1),
                AckTime       = now.AddHours(-1).AddMilliseconds(i * 1000),
            });
        }
        await _db.SaveChangesAsync();

        var svc = new SloService(_db, _opts);
        var status = await svc.GetStatusAsync();

        // P99 of 100 items = item at index 98 (0-based), i.e. i=99 → 99000 ms
        status.LatencyP99Ms.Should().Be(99000);
        status.LatencyP99Met.Should().BeFalse(); // 99000 > 5000 ms target
    }
}
