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
