using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Pagination;

namespace MSOSync.Metadata.Pagination;

/// <summary>
/// Singleton that loads the HMAC key from <c>Pagination:CursorHmacKey</c> (base64, min 16 bytes)
/// and delegates to <see cref="CursorToken"/> for signed encode/decode.
/// </summary>
public sealed class CursorSigner
{
    private readonly byte[] _key;

    public CursorSigner(IConfiguration configuration, ILogger<CursorSigner> logger)
    {
        var b64 = configuration["Pagination:CursorHmacKey"]
            ?? throw new InvalidOperationException(
                "Pagination:CursorHmacKey is not configured. " +
                "Add it to appsettings.json as a base64-encoded key (minimum 16 bytes, recommend 32).");
        _key = Convert.FromBase64String(b64);
        if (_key.Length < 16)
            throw new InvalidOperationException(
                "Pagination:CursorHmacKey must decode to at least 16 bytes.");

        if (_key.All(b => b == 0))
            logger.LogWarning(
                "CursorSigner: HMAC key is the default all-zeros dev key. " +
                "Set Pagination:CursorHmacKey to a random 32-byte base64 value in production.");
    }

    /// <summary>Test-only constructor. Pass <c>new byte[32]</c> for a zeroed dev key.</summary>
    internal CursorSigner(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 16) throw new ArgumentException("HMAC key must be at least 16 bytes.", nameof(key));
        _key = key;
    }

    public string Encode(long id, long ticks) => CursorToken.Encode(id, ticks, _key);
    public (long Id, long Ticks) Decode(string token) => CursorToken.Decode(token, _key);
}
