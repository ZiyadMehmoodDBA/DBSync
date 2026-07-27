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
