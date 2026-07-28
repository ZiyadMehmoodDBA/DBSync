using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Metrics;

namespace MSOSync.Transport;

public sealed class NodeHttpClient(
    HttpClient                   httpClient,
    ICompressionNegotiator       negotiator,
    IOptions<CompressionOptions> compressionOptions,
    IMetricsService              metrics,
    GzipCompressionService       gzipService,
    BrotliCompressionService     brotliService,
    IHttpContextAccessor?        httpContextAccessor = null) : INodeHttpClient
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(TransportJsonContext.Default.Options);

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var response = await SendAsync(url, body, nodeId, nodeToken, ct);
        response.EnsureSuccessStatusCode();
        var json = await ReadBodyAsync(response, ct);
        return JsonSerializer.Deserialize<TResponse>(json, JsonOpts)!;
    }

    public async Task<TResponse?> PostNullableAsync<TRequest, TResponse>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var response = await SendAsync(url, body, nodeId, nodeToken, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        response.EnsureSuccessStatusCode();
        var json = await ReadBodyAsync(response, ct);
        return JsonSerializer.Deserialize<TResponse>(json, JsonOpts);
    }

    public async Task PostVoidAsync<TRequest>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var response = await SendAsync(url, body, nodeId, nodeToken, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var bytes    = await response.Content.ReadAsByteArrayAsync(ct);
        var encoding = response.Content.Headers.ContentEncoding;

        // I4: use the injected singleton instances rather than newing them up per response.
        if (encoding.Contains("gzip"))
        {
            bytes = gzipService.Decompress(bytes);
        }
        else if (encoding.Contains("br"))
        {
            bytes = brotliService.Decompress(bytes);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<HttpResponseMessage> SendAsync<TRequest>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        using var activity = PipelineActivitySource.Source.StartActivity("sync.send");
        activity?.SetTag("node.id", nodeId);
        try
        {
            var json      = JsonSerializer.Serialize(body, JsonOpts);
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var threshold = compressionOptions.Value.ThresholdBytes;

            byte[] outBytes;
            string? contentEncoding = null;

            if (jsonBytes.Length >= threshold)
            {
                // Apply compression and record timing
                var compressionSvc = negotiator.SelectFor(nodeId);
                var sw = Stopwatch.StartNew();
                outBytes = compressionSvc.Compress(jsonBytes);
                sw.Stop();
                metrics.RecordHistogram(
                    "sync.pipeline.compress_ms",
                    sw.Elapsed.TotalMilliseconds,
                    new Dictionary<string, string> { ["node_id"] = nodeId, ["encoding"] = compressionSvc.EncodingName });
                contentEncoding = compressionSvc.EncodingName;
            }
            else
            {
                // Below threshold — send raw; no Content-Encoding header
                outBytes = jsonBytes;
            }

            var content = new ByteArrayContent(outBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (contentEncoding is not null)
                content.Headers.ContentEncoding.Add(contentEncoding);

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Add("X-Node-Id",       nodeId);
            request.Headers.Add("X-Node-Token",    nodeToken);
            request.Headers.Add("Accept-Encoding", "gzip, br");

            var correlationId = GetOrCreateCorrelationId();
            request.Headers.Add("X-Correlation-Id", correlationId);

            var response = await httpClient.SendAsync(request, ct);
            activity?.SetTag("http.status_code", (int)response.StatusCode);
            activity?.SetStatus(response.IsSuccessStatusCode ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private string GetOrCreateCorrelationId()
    {
        var ctx = httpContextAccessor?.HttpContext;
        if (ctx != null && ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var id))
            return id.ToString();
        return Guid.NewGuid().ToString("N");
    }
}
