using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using MSOSync.Routing;

namespace MSOSync.Benchmarks;

/// <summary>
/// Measures IBulkRoutingService.FanOutAsync at 1000 active nodes.
/// Target: P95 &lt; 100 ms for a single bulk insert vs N individual inserts baseline.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class BulkFanOutBenchmark
{
    private BulkRoutingService _svc = null!;
    private long _seq = 1;

    [GlobalSetup]
    public async Task Setup()
    {
        await BenchmarkDbSeeder.EnsureSeededAsync();
        _svc = new BulkRoutingService(BenchmarkDbSeeder.CreateDb());
    }

    [Benchmark]
    public async Task FanOut_1000Nodes_SingleBulkInsert()
    {
        // Each benchmark iteration inserts to all eligible nodes.
        // We increment batchSequence to avoid PK conflicts.
        _ = await _svc.FanOutAsync(
            triggerId:     "trig-bench-01",
            channelId:     "ch-bench",
            batchSequence: Interlocked.Increment(ref _seq),
            rowCount:      100,
            byteCount:     4096L,
            tenantId:      BenchmarkDbSeeder.TenantId);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        // Remove inserted batch rows between benchmark runs to keep table small
        var db = BenchmarkDbSeeder.CreateDb();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [msosync].[sync_outgoing_batch] WHERE [tenant_id] = @p0",
            BenchmarkDbSeeder.TenantId);
    }
}
