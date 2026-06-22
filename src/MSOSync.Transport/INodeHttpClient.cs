namespace MSOSync.Transport;

public interface INodeHttpClient
{
    /// <summary>
    /// POST JSON body (gzip-compressed) and deserialize response.
    /// Throws HttpRequestException on non-success status (after Polly retries).
    /// </summary>
    Task<TResponse> PostAsync<TRequest, TResponse>(
        string            url,
        TRequest          body,
        string            nodeId,
        string            nodeToken,
        CancellationToken ct = default);

    /// <summary>
    /// POST and return null on 204 No Content.
    /// </summary>
    Task<TResponse?> PostNullableAsync<TRequest, TResponse>(
        string            url,
        TRequest          body,
        string            nodeId,
        string            nodeToken,
        CancellationToken ct = default);
}
