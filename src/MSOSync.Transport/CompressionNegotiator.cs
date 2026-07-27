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
