using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MSOSync.Common.Pagination;
using Xunit;

namespace MSOSync.MetadataTests.Pagination;

public sealed class CursorTokenTests
{
    private static readonly byte[] TestKey = new byte[32]; // all-zeros dev key

    [Fact]
    public void Encode_ThenDecode_ReturnsOriginalValues()
    {
        var token = CursorToken.Encode(12345L, 637800000000000000L, TestKey);
        var (id, ticks) = CursorToken.Decode(token, TestKey);
        id.Should().Be(12345L);
        ticks.Should().Be(637800000000000000L);
    }

    [Fact]
    public void Encode_ProducesOpaqueToken_NotRawText()
    {
        var token = CursorToken.Encode(1L, 0L, TestKey);
        token.Should().NotContain("v2:1:0");
    }

    [Fact]
    public void Decode_GarbageInput_ThrowsArgumentException()
    {
        var act = () => CursorToken.Decode("not-valid-base64!!!", TestKey);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_WrongVersion_ThrowsArgumentException()
    {
        // Craft a v1 token with no HMAC — should be rejected
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes("v1:1:0"));
        var act = () => CursorToken.Decode(raw, TestKey);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_TamperedPayload_ThrowsArgumentException()
    {
        // Encode legit token, then tamper with the id field in the raw payload
        var legit = CursorToken.Encode(1L, 1000L, TestKey);
        var raw = Encoding.UTF8.GetString(Convert.FromBase64String(legit));
        // raw is "v2:1:1000:<hmac>"; replace id 1 with 999
        var tampered = raw.Replace("v2:1:1000:", "v2:999:1000:");
        var tamperedToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(tampered));

        var act = () => CursorToken.Decode(tamperedToken, TestKey);
        act.Should().Throw<ArgumentException>().WithMessage("*signature*");
    }

    [Fact]
    public void Decode_WrongKey_ThrowsArgumentException()
    {
        var token = CursorToken.Encode(1L, 0L, TestKey);
        var wrongKey = new byte[32];
        wrongKey[0] = 1; // one byte different

        var act = () => CursorToken.Decode(token, wrongKey);
        act.Should().Throw<ArgumentException>().WithMessage("*signature*");
    }

    [Fact]
    public void Encode_ZeroValues_RoundTrips()
    {
        var token = CursorToken.Encode(0L, 0L, TestKey);
        var (id, ticks) = CursorToken.Decode(token, TestKey);
        id.Should().Be(0L);
        ticks.Should().Be(0L);
    }

    [Fact]
    public void Encode_MaxLong_RoundTrips()
    {
        var token = CursorToken.Encode(long.MaxValue, long.MaxValue, TestKey);
        var (id, ticks) = CursorToken.Decode(token, TestKey);
        id.Should().Be(long.MaxValue);
        ticks.Should().Be(long.MaxValue);
    }
}
