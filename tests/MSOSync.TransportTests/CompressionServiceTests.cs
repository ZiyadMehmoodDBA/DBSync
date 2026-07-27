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
