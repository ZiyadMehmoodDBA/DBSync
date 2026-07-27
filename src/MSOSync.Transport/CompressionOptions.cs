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
