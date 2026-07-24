using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Metadata.Pagination;
using MSOSync.Metadata.Topology;

namespace MSOSync.Benchmarks;

/// <summary>
/// Measures GetTopologyGraphAsync at 1000 nodes / 200 groups / 400 routers.
/// Target: P95 &lt; 500 ms.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class TopologyGraphBenchmark
{
    private TopologyQueryService _svc = null!;
    private IMemoryCache         _cache = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        await BenchmarkDbSeeder.EnsureSeededAsync();
        _cache = new MemoryCache(new MemoryCacheOptions());
        var signer = new CursorSigner(new byte[32]);
        _svc = new TopologyQueryService(BenchmarkDbSeeder.CreateDb(), _cache, signer);
    }

    [Benchmark]
    public async Task GetTopologyGraph_1000Nodes()
    {
        // Clear cache before each iteration so we measure DB round-trips
        _cache.Remove("topology:graph");
        _ = await _svc.GetTopologyGraphAsync(null, default);
    }

    [Benchmark]
    public async Task GetTopologyGraph_WithNodeIdFilter()
    {
        // Filter to a 50-node subgraph
        var filter = Enumerable.Range(1, 50).Select(i => $"node-{i:D4}").ToArray();
        _ = await _svc.GetTopologyGraphAsync(filter, default);
    }
}
