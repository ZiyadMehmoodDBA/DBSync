using BenchmarkDotNet.Attributes;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common.Caching;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Pagination;
using MSOSync.Metadata.Services;
using MSOSync.Security;

namespace MSOSync.Benchmarks;

/// <summary>
/// Measures cursor page retrieval at 1000 nodes.
/// Target: P95 &lt; 50 ms per page.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class NodeCursorPageBenchmark
{
    private NodeMetadataService _svc    = null!;
    private string?             _page5Cursor;
    private string?             _page20Cursor;

    [GlobalSetup]
    public async Task Setup()
    {
        await BenchmarkDbSeeder.EnsureSeededAsync();

        var db     = BenchmarkDbSeeder.CreateDb();
        var memCache = new MemoryCache(new MemoryCacheOptions());
        var cache  = new InMemoryCacheService(memCache, Options.Create(new CacheOptions()));
        var signer = new CursorSigner(new byte[32]);

        var protMock = new Mock<IDataProtector>();
        protMock.Setup(p => p.Protect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        protMock.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        var dpMock = new Mock<IDataProtectionProvider>();
        dpMock.Setup(dp => dp.CreateProtector(It.IsAny<string>())).Returns(protMock.Object);

        _svc = new NodeMetadataService(db, cache, new Mock<IMediator>().Object,
            new NodeSecurityService(db, new BCryptPasswordHasher()), dpMock.Object, signer);

        // Pre-compute cursors for page 5 and page 20 (pageSize = 50)
        string? cursor = null;
        for (int page = 1; page <= 20; page++)
        {
            var result = await _svc.GetNodesCursorAsync(
                new NodeCursorFilter { PageSize = 50, Cursor = cursor }, default);
            if (page == 4)  _page5Cursor  = result.NextCursor;
            if (page == 19) _page20Cursor = result.NextCursor;
            cursor = result.NextCursor;
            if (!result.HasMore) break;
        }
    }

    [Benchmark]
    public async Task FirstPage()
        => _ = await _svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 50, Cursor = null }, default);

    [Benchmark]
    public async Task Page5()
        => _ = await _svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 50, Cursor = _page5Cursor }, default);

    [Benchmark]
    public async Task Page20()
        => _ = await _svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 50, Cursor = _page20Cursor }, default);
}
