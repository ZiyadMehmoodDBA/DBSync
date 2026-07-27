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
