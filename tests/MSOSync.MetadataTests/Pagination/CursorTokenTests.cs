using FluentAssertions;
using MSOSync.Common.Pagination;
using Xunit;

namespace MSOSync.MetadataTests.Pagination;

public sealed class CursorTokenTests
{
    [Fact]
    public void Encode_ThenDecode_ReturnsOriginalValues()
    {
        var token = CursorToken.Encode(12345L, 637800000000000000L);
        var (id, ticks) = CursorToken.Decode(token);
        id.Should().Be(12345L);
        ticks.Should().Be(637800000000000000L);
    }

    [Fact]
    public void Encode_ProducesOpaqueBase64()
    {
        var token = CursorToken.Encode(1L, 0L);
        Convert.FromBase64String(token).Should().NotBeEmpty(); // valid base64
        token.Should().NotContain("1");  // opaque — raw id not visible
    }

    [Fact]
    public void Decode_GarbageInput_ThrowsArgumentException()
    {
        var act = () => CursorToken.Decode("not-valid-base64!!!");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_WrongVersion_ThrowsArgumentException()
    {
        var raw = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("v2:1:0"));
        var act = () => CursorToken.Decode(raw);
        act.Should().Throw<ArgumentException>().WithMessage("*format*");
    }

    [Fact]
    public void Decode_NonNumericId_ThrowsArgumentException()
    {
        var raw = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("v1:abc:0"));
        var act = () => CursorToken.Decode(raw);
        act.Should().Throw<ArgumentException>().WithMessage("*values*");
    }

    [Fact]
    public void Encode_ZeroValues_RoundTrips()
    {
        var token = CursorToken.Encode(0L, 0L);
        var (id, ticks) = CursorToken.Decode(token);
        id.Should().Be(0L);
        ticks.Should().Be(0L);
    }

    [Fact]
    public void Encode_MaxLong_RoundTrips()
    {
        var token = CursorToken.Encode(long.MaxValue, long.MaxValue);
        var (id, ticks) = CursorToken.Decode(token);
        id.Should().Be(long.MaxValue);
        ticks.Should().Be(long.MaxValue);
    }
}
