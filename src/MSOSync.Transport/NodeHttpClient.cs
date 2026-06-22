using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace MSOSync.Transport;

public sealed class NodeHttpClient(
    HttpClient             httpClient,
    GzipCompressionService compression,
    IHttpContextAccessor?  httpContextAccessor = null) : INodeHttpClient
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(TransportJsonContext.Default.Options);

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var response = await SendAsync(url, body, nodeId, nodeToken, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<TResponse>(json, JsonOpts)!;
    }

    public async Task<TResponse?> PostNullableAsync<TRequest, TResponse>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var response = await SendAsync(url, body, nodeId, nodeToken, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<TResponse>(json, JsonOpts);
    }

    private async Task<HttpResponseMessage> SendAsync<TRequest>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var json       = JsonSerializer.Serialize(body, JsonOpts);
        var jsonBytes  = Encoding.UTF8.GetBytes(json);
        var compressed = compression.Compress(jsonBytes);

        var content = new ByteArrayContent(compressed);
        content.Headers.ContentType     = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("X-Node-Id",    nodeId);
        request.Headers.Add("X-Node-Token", nodeToken);
        request.Headers.Add("Accept-Encoding", "gzip");

        var correlationId = GetOrCreateCorrelationId();
        request.Headers.Add("X-Correlation-Id", correlationId);

        return await httpClient.SendAsync(request, ct);
    }

    private string GetOrCreateCorrelationId()
    {
        var ctx = httpContextAccessor?.HttpContext;
        if (ctx != null && ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var id))
            return id.ToString();
        return Guid.NewGuid().ToString("N");
    }
}
