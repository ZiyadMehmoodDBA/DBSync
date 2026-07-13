using Microsoft.Extensions.Configuration;
using MSOSync.Common.Pagination;

namespace MSOSync.Metadata.Pagination;

/// <summary>
/// Singleton that loads the HMAC key from <c>Pagination:CursorHmacKey</c> (base64, min 16 bytes)
/// and delegates to <see cref="CursorToken"/> for signed encode/decode.
/// </summary>
public sealed class CursorSigner
{
    private readonly byte[] _key;

    public CursorSigner(IConfiguration configuration)
    {
        var b64 = configuration["Pagination:CursorHmacKey"]
            ?? throw new InvalidOperationException(
                "Pagination:CursorHmacKey is not configured. " +
                "Add it to appsettings.json as a base64-encoded key (minimum 16 bytes, recommend 32).");
        _key = Convert.FromBase64String(b64);
        if (_key.Length < 16)
            throw new InvalidOperationException(
                "Pagination:CursorHmacKey must decode to at least 16 bytes.");
    }

    /// <summary>Test-only constructor. Pass <c>new byte[32]</c> for a zeroed dev key.</summary>
    public CursorSigner(byte[] key) => _key = key;

    public string Encode(long id, long ticks) => CursorToken.Encode(id, ticks, _key);
    public (long Id, long Ticks) Decode(string token) => CursorToken.Decode(token, _key);
}
