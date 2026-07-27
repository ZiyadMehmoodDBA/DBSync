using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common;
using MSOSync.Transport;
using Xunit;

namespace MSOSync.TransportTests;

/// <summary>
/// Tests that NodeHttpClient applies or skips compression based on ThresholdBytes.
/// Uses a captured-request HttpMessageHandler to inspect the outgoing request.
/// </summary>
public sealed class CompressionGateTests : IDisposable
{
    private HttpRequestMessage? _capturedRequest;
    private byte[]? _capturedBody;

    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private NodeHttpClient BuildClient(int thresholdBytes)
    {
        var opts = Options.Create(new CompressionOptions { ThresholdBytes = thresholdBytes, Level = CompressionLevelOption.Fastest });

        var gzip   = new GzipCompressionService(opts);
        var brotli = new BrotliCompressionService(opts);
        var negotiator = new CompressionNegotiator(_cache, gzip, brotli);
        var metrics = Mock.Of<IMetricsService>();

        var handler = new CapturingHandler(req =>
        {
            _capturedRequest = req;
            _capturedBody    = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake-node/") };
        return new NodeHttpClient(httpClient, negotiator, opts, metrics);
    }

    [Fact]
    public async Task BelowThreshold_NoContentEncodingHeader()
    {
        var client  = BuildClient(thresholdBytes: 1024);
        var payload = new string('x', 512); // 512 bytes < 1024 threshold

        // PostVoidAsync will throw (handler returns nothing), but we only need the captured request
        await Assert.ThrowsAnyAsync<Exception>(
            () => client.PostVoidAsync<string>("http://fake-node/api/v1/sync/push", payload, "node-1", "tok", CancellationToken.None));

        _capturedRequest!.Content!.Headers.ContentEncoding.Should().BeEmpty();
    }

    [Fact]
    public async Task AboveThreshold_GzipContentEncodingHeader()
    {
        var client  = BuildClient(thresholdBytes: 1024);
        var payload = new string('x', 2048); // 2048 bytes > 1024 threshold (after JSON serialisation ~2052 bytes)

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.PostVoidAsync<string>("http://fake-node/api/v1/sync/push", payload, "node-1", "tok", CancellationToken.None));

        _capturedRequest!.Content!.Headers.ContentEncoding.Should().Contain("gzip");
    }

    [Fact]
    public async Task AtThreshold_CompressionIsApplied()
    {
        // Threshold is inclusive lower bound — payload at exactly threshold bytes triggers compression
        // JSON serialisation adds surrounding quotes: 1022 chars + 2 = 1024 bytes
        var client  = BuildClient(thresholdBytes: 1024);
        var payload = new string('y', 1022); // "\"yyyy...\"" = 1024 bytes in JSON

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.PostVoidAsync<string>("http://fake-node/api/v1/sync/push", payload, "node-1", "tok", CancellationToken.None));

        _capturedRequest!.Content!.Headers.ContentEncoding.Should().Contain("gzip");
    }

    [Fact]
    public async Task AboveThreshold_BrotliNodeCapability_UsesBrotliEncoding()
    {
        // Register brotli capability in cache for node-br
        _cache.Set("node-compression:node-br", new[] { "gzip", "br" });
        var client  = BuildClient(thresholdBytes: 64);
        var payload = new string('z', 100); // 100 bytes > 64 threshold

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.PostVoidAsync<string>("http://fake-node/api/v1/sync/push", payload, "node-br", "tok", CancellationToken.None));

        _capturedRequest!.Content!.Headers.ContentEncoding.Should().Contain("br");
    }

    public void Dispose() => _cache.Dispose();

    // -----------------------------------------------------------------------
    // Minimal capturing HttpMessageHandler
    // -----------------------------------------------------------------------

    private sealed class CapturingHandler(Action<HttpRequestMessage> capture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(request);
            // Return 500 so the client's EnsureSuccessStatusCode throws — that's fine for our tests
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
