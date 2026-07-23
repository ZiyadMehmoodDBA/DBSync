# Task 1: `IMetricsService` + `ICompressionService` Abstraction

> Part of [Phase 2D.5 Master Plan](2026-07-23-phase-2D-5-master.md)

**Goal:** Introduce the `IMetricsService` interface and its `InMemoryMetricsService` default implementation, then abstract compression behind `ICompressionService` so `GzipCompressionService` and new `BrotliCompressionService` share a common contract.

**Files:**
- Create: `src/MSOSync.Common/IMetricsService.cs`
- Create: `src/MSOSync.Common/InMemoryMetricsService.cs`
- Create: `src/MSOSync.Transport/ICompressionService.cs`
- Create: `src/MSOSync.Transport/CompressionOptions.cs`
- Create: `src/MSOSync.Transport/BrotliCompressionService.cs`
- Create: `src/MSOSync.Transport/ICompressionNegotiator.cs`
- Create: `src/MSOSync.Transport/CompressionNegotiator.cs`
- Modify: `src/MSOSync.Transport/GzipCompressionService.cs`
- Modify: `src/MSOSync.Transport/TransportServiceExtensions.cs`
- Create: `tests/MSOSync.TransportTests/CompressionServiceTests.cs`
- Create: `tests/MSOSync.TransportTests/CompressionNegotiatorTests.cs`
- Modify: `tests/MSOSync.TransportTests/GzipCompressionServiceTests.cs`

**Interfaces:**
- Produces: `IMetricsService` in `MSOSync.Common` — `void RecordHistogram(string name, double valueMs, IReadOnlyDictionary<string, string>? tags = null)` and `void IncrementCounter(string name, IReadOnlyDictionary<string, string>? tags = null)`
- Produces: `ICompressionService` in `MSOSync.Transport` — `string EncodingName`, `byte[] Compress(byte[] data)`, `byte[] Decompress(byte[] data)`
- Produces: `CompressionOptions` bound from `"Compression"` config section
- Produces: `ICompressionNegotiator` in `MSOSync.Transport` — `ICompressionService SelectFor(string nodeId)`
- Consumes: nothing from earlier tasks

---

- [ ] **Step 1: Write failing tests for `InMemoryMetricsService`**

Create `tests/MSOSync.Tests/InMemoryMetricsServiceTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Common;
using Xunit;

namespace MSOSync.Tests;

public sealed class InMemoryMetricsServiceTests
{
    private readonly InMemoryMetricsService _svc = new();

    [Fact]
    public void RecordHistogram_StoresValue_RetrievableViaSnapshot()
    {
        _svc.RecordHistogram("sync.pipeline.fetch_ms", 42.5);
        var snap = _svc.GetSnapshot("sync.pipeline.fetch_ms");
        snap.Should().ContainSingle().Which.Should().BeApproximately(42.5, 0.001);
    }

    [Fact]
    public void RecordHistogram_RingBufferCap_OldestEntryEvicted()
    {
        for (int i = 0; i < 1001; i++)
            _svc.RecordHistogram("test.hist", i);
        var snap = _svc.GetSnapshot("test.hist");
        snap.Should().HaveCount(1000);
        snap[0].Should().BeApproximately(1.0, 0.001); // first entry (0) evicted
    }

    [Fact]
    public void IncrementCounter_AccumulatesCorrectly()
    {
        _svc.IncrementCounter("test.counter");
        _svc.IncrementCounter("test.counter");
        _svc.GetCounterValue("test.counter").Should().Be(2);
    }

    [Fact]
    public void RecordHistogram_UnknownName_ReturnsEmpty()
    {
        _svc.GetSnapshot("does.not.exist").Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/MSOSync.Tests/MSOSync.Tests.csproj --filter "InMemoryMetricsServiceTests" -v m
```

Expected: compile error — `InMemoryMetricsService` does not exist yet.

- [ ] **Step 3: Create `IMetricsService` interface**

Create `src/MSOSync.Common/IMetricsService.cs`:

```csharp
namespace MSOSync.Common;

/// <summary>
/// Lightweight metrics sink. Records named histograms for pipeline stage timing.
/// Phase 2F will replace InMemoryMetricsService with an OpenTelemetry-backed implementation.
/// </summary>
public interface IMetricsService
{
    /// <summary>Record a duration in milliseconds against a named histogram.</summary>
    void RecordHistogram(string name, double valueMs, IReadOnlyDictionary<string, string>? tags = null);

    /// <summary>Increment a named counter.</summary>
    void IncrementCounter(string name, IReadOnlyDictionary<string, string>? tags = null);
}
```

- [ ] **Step 4: Create `InMemoryMetricsService`**

Create `src/MSOSync.Common/InMemoryMetricsService.cs`:

```csharp
using System.Collections.Concurrent;

namespace MSOSync.Common;

/// <summary>
/// Thread-safe in-memory histogram and counter store.
/// Each histogram is a ring buffer capped at 1 000 entries to bound memory.
/// Phase 2F replaces this with an OpenTelemetry-backed implementation.
/// </summary>
public sealed class InMemoryMetricsService : IMetricsService
{
    private const int RingBufferCap = 1000;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<double>> _histograms = new();
    private readonly ConcurrentDictionary<string, long>                    _counters   = new();

    public void RecordHistogram(string name, double valueMs, IReadOnlyDictionary<string, string>? tags = null)
    {
        var queue = _histograms.GetOrAdd(name, _ => new ConcurrentQueue<double>());
        queue.Enqueue(valueMs);
        // Evict oldest when over cap
        while (queue.Count > RingBufferCap)
            queue.TryDequeue(out _);
    }

    public void IncrementCounter(string name, IReadOnlyDictionary<string, string>? tags = null)
        => _counters.AddOrUpdate(name, 1L, (_, v) => v + 1);

    /// <summary>Returns a point-in-time snapshot of recorded values (oldest first).</summary>
    public double[] GetSnapshot(string name)
        => _histograms.TryGetValue(name, out var q) ? q.ToArray() : Array.Empty<double>();

    /// <summary>Returns the current counter value (0 if not yet incremented).</summary>
    public long GetCounterValue(string name)
        => _counters.TryGetValue(name, out var v) ? v : 0L;
}
```

- [ ] **Step 5: Run metrics tests to verify they pass**

```bash
dotnet test tests/MSOSync.Tests/MSOSync.Tests.csproj --filter "InMemoryMetricsServiceTests" -v m
```

Expected: 4 tests PASS.

- [ ] **Step 6: Commit metrics foundation**

```bash
git add src/MSOSync.Common/IMetricsService.cs src/MSOSync.Common/InMemoryMetricsService.cs tests/MSOSync.Tests/InMemoryMetricsServiceTests.cs
git commit -m "feat(2D.5-T1): add IMetricsService + InMemoryMetricsService ring-buffer impl"
```

- [ ] **Step 7: Create `ICompressionService` interface**

Create `src/MSOSync.Transport/ICompressionService.cs`:

```csharp
namespace MSOSync.Transport;

/// <summary>
/// Content-encoding-agnostic compression contract.
/// Implementations: GzipCompressionService, BrotliCompressionService.
/// </summary>
public interface ICompressionService
{
    /// <summary>
    /// The HTTP Content-Encoding token this implementation produces (e.g. "gzip", "br").
    /// </summary>
    string EncodingName { get; }

    /// <summary>Compress <paramref name="data"/> using the configured compression level.</summary>
    byte[] Compress(byte[] data);

    /// <summary>Decompress <paramref name="data"/> compressed with this encoding.</summary>
    byte[] Decompress(byte[] data);
}
```

- [ ] **Step 8: Create `CompressionOptions`**

Create `src/MSOSync.Transport/CompressionOptions.cs`:

```csharp
namespace MSOSync.Transport;

public sealed class CompressionOptions
{
    public const string Section = "Compression";

    /// <summary>
    /// Payloads smaller than this byte count are sent uncompressed.
    /// Default 1024 bytes: gzip header overhead plus CPU cost exceeds savings for small payloads.
    /// </summary>
    public int ThresholdBytes { get; init; } = 1024;

    /// <summary>
    /// Compression level applied when gzip or brotli is used.
    /// Fastest: lowest CPU, ~60% ratio. Optimal: balanced. SmallestSize: highest CPU, best ratio.
    /// </summary>
    public CompressionLevelOption Level { get; init; } = CompressionLevelOption.Fastest;
}

/// <summary>Maps to System.IO.Compression.CompressionLevel without a direct dependency on it in options.</summary>
public enum CompressionLevelOption
{
    Fastest,
    Optimal,
    SmallestSize
}
```

- [ ] **Step 9: Update `GzipCompressionService` to implement `ICompressionService`**

Replace `src/MSOSync.Transport/GzipCompressionService.cs` entirely:

```csharp
using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace MSOSync.Transport;

public sealed class GzipCompressionService : ICompressionService
{
    private readonly CompressionLevel _level;

    public GzipCompressionService(IOptions<CompressionOptions> options)
        => _level = MapLevel(options.Value.Level);

    public string EncodingName => "gzip";

    public byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, _level, leaveOpen: true))
            gzip.Write(data, 0, data.Length);
        return output.ToArray();
    }

    public byte[] Decompress(byte[] data)
    {
        using var input  = new MemoryStream(data);
        using var gzip   = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static CompressionLevel MapLevel(CompressionLevelOption opt) => opt switch
    {
        CompressionLevelOption.Fastest      => CompressionLevel.Fastest,
        CompressionLevelOption.SmallestSize => CompressionLevel.SmallestSize,
        _                                   => CompressionLevel.Optimal
    };
}
```

- [ ] **Step 10: Create `BrotliCompressionService`**

Create `src/MSOSync.Transport/BrotliCompressionService.cs`:

```csharp
using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace MSOSync.Transport;

public sealed class BrotliCompressionService : ICompressionService
{
    private readonly CompressionLevel _level;

    public BrotliCompressionService(IOptions<CompressionOptions> options)
        => _level = MapLevel(options.Value.Level);

    public string EncodingName => "br";

    public byte[] Compress(byte[] data)
    {
        using var output  = new MemoryStream();
        using (var brotli = new BrotliStream(output, _level, leaveOpen: true))
            brotli.Write(data, 0, data.Length);
        return output.ToArray();
    }

    public byte[] Decompress(byte[] data)
    {
        using var input  = new MemoryStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    private static CompressionLevel MapLevel(CompressionLevelOption opt) => opt switch
    {
        CompressionLevelOption.Fastest      => CompressionLevel.Fastest,
        CompressionLevelOption.SmallestSize => CompressionLevel.SmallestSize,
        _                                   => CompressionLevel.Optimal
    };
}
```

- [ ] **Step 11: Create `ICompressionNegotiator` interface**

Create `src/MSOSync.Transport/ICompressionNegotiator.cs`:

```csharp
namespace MSOSync.Transport;

/// <summary>
/// Selects the appropriate ICompressionService for a given target node
/// based on the node's advertised compression capabilities from its most recent heartbeat.
/// </summary>
public interface ICompressionNegotiator
{
    /// <summary>
    /// Returns the best ICompressionService for the given node.
    /// Falls back to gzip if the node has not advertised capabilities
    /// or if the advertised algorithm is unsupported.
    /// </summary>
    ICompressionService SelectFor(string nodeId);
}
```

- [ ] **Step 12: Create `CompressionNegotiator`**

Create `src/MSOSync.Transport/CompressionNegotiator.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;

namespace MSOSync.Transport;

/// <summary>
/// Reads node compression capabilities from IMemoryCache (keyed "node-compression:{nodeId}").
/// Priority: brotli > gzip. Falls back to gzip when no capability cached.
/// </summary>
public sealed class CompressionNegotiator(
    IMemoryCache             cache,
    ICompressionService      gzip,
    BrotliCompressionService brotli) : ICompressionNegotiator
{
    // Cache key pattern matches what the heartbeat handler stores
    private static string CacheKey(string nodeId) => $"node-compression:{nodeId}";

    public ICompressionService SelectFor(string nodeId)
    {
        if (cache.TryGetValue(CacheKey(nodeId), out string[]? encodings) && encodings != null)
        {
            if (encodings.Contains("br",   StringComparer.OrdinalIgnoreCase)) return brotli;
            if (encodings.Contains("gzip", StringComparer.OrdinalIgnoreCase)) return gzip;
        }
        return gzip; // default
    }
}
```

- [ ] **Step 13: Update `TransportServiceExtensions` to register the new types**

Replace `src/MSOSync.Transport/TransportServiceExtensions.cs` entirely:

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using MSOSync.Engine;

namespace MSOSync.Transport;

public static class TransportServiceExtensions
{
    public static IServiceCollection AddTransportServices(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Options
        services.Configure<CompressionOptions>(config.GetSection(CompressionOptions.Section));

        // Compression
        services.AddMemoryCache();
        services.AddSingleton<ICompressionService, GzipCompressionService>();
        services.AddSingleton<BrotliCompressionService>();
        services.AddSingleton<ICompressionNegotiator, CompressionNegotiator>();
        services.AddSingleton<ITransportFailureClassifier, TransportFailureClassifier>();

        // Typed HttpClient with Polly resilience
        services.AddHttpClient<NodeHttpClient>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.MinimumThroughput = 5;
            });

        services.AddScoped<INodeHttpClient, NodeHttpClient>();

        // Transport services (scoped — one per request / scope)
        services.AddScoped<PushClient>();
        services.AddScoped<PullClient>();
        services.AddScoped<AcknowledgementService>();
        services.AddScoped<ITransportService, SmartTransportService>();

        return services;
    }
}
```

Note: `IMemoryCache` registration via `AddMemoryCache()` is idempotent — safe to call multiple times.

- [ ] **Step 14: Write compression service tests**

Create `tests/MSOSync.TransportTests/CompressionServiceTests.cs`:

```csharp
using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MSOSync.Transport;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class CompressionServiceTests
{
    private static IOptions<CompressionOptions> Opts(CompressionLevelOption level = CompressionLevelOption.Fastest)
        => Options.Create(new CompressionOptions { Level = level });

    private static byte[] RepeatBytes(int count)
    {
        var data = new byte[count];
        // Fill with repeating pattern so compression has something to work with
        for (int i = 0; i < count; i++) data[i] = (byte)(i % 64);
        return data;
    }

    [Fact]
    public void GzipCompressionService_RoundTrip_MatchesOriginal()
    {
        ICompressionService svc = new GzipCompressionService(Opts());
        var original = RepeatBytes(4096);
        svc.Decompress(svc.Compress(original)).Should().Equal(original);
    }

    [Fact]
    public void BrotliCompressionService_RoundTrip_MatchesOriginal()
    {
        ICompressionService svc = new BrotliCompressionService(Opts());
        var original = RepeatBytes(4096);
        svc.Decompress(svc.Compress(original)).Should().Equal(original);
    }

    [Fact]
    public void GzipCompressionService_EncodingName_IsGzip()
    {
        ICompressionService svc = new GzipCompressionService(Opts());
        svc.EncodingName.Should().Be("gzip");
    }

    [Fact]
    public void BrotliCompressionService_EncodingName_IsBr()
    {
        ICompressionService svc = new BrotliCompressionService(Opts());
        svc.EncodingName.Should().Be("br");
    }

    [Fact]
    public void GzipCompressionService_SmallestSize_OutputSmallerThanFastest()
    {
        var original  = RepeatBytes(4096);
        var fastest   = new GzipCompressionService(Opts(CompressionLevelOption.Fastest)).Compress(original);
        var smallest  = new GzipCompressionService(Opts(CompressionLevelOption.SmallestSize)).Compress(original);
        // For a compressible repeating pattern, SmallestSize <= Fastest
        smallest.Length.Should().BeLessThanOrEqualTo(fastest.Length);
    }

    [Fact]
    public void GzipCompressionService_EmptyArray_RoundTrip()
    {
        ICompressionService svc = new GzipCompressionService(Opts());
        svc.Decompress(svc.Compress(Array.Empty<byte>())).Should().BeEmpty();
    }
}
```

- [ ] **Step 15: Write compression negotiator tests**

Create `tests/MSOSync.TransportTests/CompressionNegotiatorTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MSOSync.Transport;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class CompressionNegotiatorTests : IDisposable
{
    private readonly IMemoryCache        _cache  = new MemoryCache(new MemoryCacheOptions());
    private readonly ICompressionService _gzip   = new GzipCompressionService(Options.Create(new CompressionOptions()));
    private readonly BrotliCompressionService _brotli = new(Options.Create(new CompressionOptions()));

    private CompressionNegotiator BuildNegotiator() => new(_cache, _gzip, _brotli);

    private void SetNodeEncodings(string nodeId, string[] encodings)
        => _cache.Set($"node-compression:{nodeId}", encodings);

    [Fact]
    public void SelectFor_NodeAdvertisesBrotliAndGzip_ReturnsBrotli()
    {
        SetNodeEncodings("node-1", ["gzip", "br"]);
        BuildNegotiator().SelectFor("node-1").EncodingName.Should().Be("br");
    }

    [Fact]
    public void SelectFor_NodeAdvertisesGzipOnly_ReturnsGzip()
    {
        SetNodeEncodings("node-2", ["gzip"]);
        BuildNegotiator().SelectFor("node-2").EncodingName.Should().Be("gzip");
    }

    [Fact]
    public void SelectFor_NodeHasNoCachedCapability_ReturnsGzip()
    {
        // No cache entry for "node-3"
        BuildNegotiator().SelectFor("node-3").EncodingName.Should().Be("gzip");
    }

    [Fact]
    public void SelectFor_NodeAdvertisesUnknownEncoding_ReturnsGzip()
    {
        SetNodeEncodings("node-4", ["deflate"]);
        BuildNegotiator().SelectFor("node-4").EncodingName.Should().Be("gzip");
    }

    public void Dispose() => _cache.Dispose();
}
```

- [ ] **Step 16: Update existing `GzipCompressionServiceTests` to construct via interface**

`GzipCompressionService` now requires `IOptions<CompressionOptions>`. Update `tests/MSOSync.TransportTests/GzipCompressionServiceTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MSOSync.Transport;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class GzipCompressionServiceTests
{
    private static readonly ICompressionService Svc =
        new GzipCompressionService(Options.Create(new CompressionOptions()));

    [Fact]
    public void CompressDecompress_RoundTrip_MatchesOriginal()
    {
        var original   = Encoding.UTF8.GetBytes("hello world from MSOSync transport");
        var compressed = Svc.Compress(original);
        var restored   = Svc.Decompress(compressed);
        restored.Should().Equal(original);
    }

    [Fact]
    public void Compress_LargePayload_RoundTrip()
    {
        var original   = Encoding.UTF8.GetBytes(new string('A', 100_000));
        var compressed = Svc.Compress(original);
        compressed.Length.Should().BeLessThan(original.Length);
        Svc.Decompress(compressed).Should().Equal(original);
    }

    [Fact]
    public void Compress_EmptyArray_RoundTrip()
    {
        var original   = Array.Empty<byte>();
        var compressed = Svc.Compress(original);
        var restored   = Svc.Decompress(compressed);
        restored.Should().Equal(original);
    }
}
```

- [ ] **Step 17: Run all Transport tests**

```bash
dotnet test tests/MSOSync.TransportTests/MSOSync.TransportTests.csproj -v m
```

Expected: all tests PASS (including updated `GzipCompressionServiceTests`, new `CompressionServiceTests`, new `CompressionNegotiatorTests`, and all pre-existing tests).

- [ ] **Step 18: Commit compression abstraction**

```bash
git add src/MSOSync.Transport/ICompressionService.cs \
        src/MSOSync.Transport/CompressionOptions.cs \
        src/MSOSync.Transport/BrotliCompressionService.cs \
        src/MSOSync.Transport/ICompressionNegotiator.cs \
        src/MSOSync.Transport/CompressionNegotiator.cs \
        src/MSOSync.Transport/GzipCompressionService.cs \
        src/MSOSync.Transport/TransportServiceExtensions.cs \
        tests/MSOSync.TransportTests/CompressionServiceTests.cs \
        tests/MSOSync.TransportTests/CompressionNegotiatorTests.cs \
        tests/MSOSync.TransportTests/GzipCompressionServiceTests.cs
git commit -m "feat(2D.5-T1): add ICompressionService abstraction, BrotliCompressionService, ICompressionNegotiator"
```
