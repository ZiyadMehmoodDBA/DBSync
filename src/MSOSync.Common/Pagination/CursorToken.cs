using System.Security.Cryptography;
using System.Text;

namespace MSOSync.Common.Pagination;

public static class CursorToken
{
    /// <summary>
    /// Encodes a cursor with HMAC-SHA256 signature to prevent tampering.
    /// Format (before outer base64): v2:{id}:{ticks}:{base64Hmac}
    /// </summary>
    public static string Encode(long id, long ticks, ReadOnlySpan<byte> hmacKey)
    {
        var payload = $"v2:{id}:{ticks}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hmac = HMACSHA256.HashData(hmacKey, payloadBytes);
        var combined = $"{payload}:{Convert.ToBase64String(hmac)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(combined));
    }

    /// <summary>
    /// Decodes and verifies a cursor. Throws <see cref="ArgumentException"/> if the token is
    /// malformed, has an invalid version, or fails HMAC verification.
    /// </summary>
    public static (long Id, long Ticks) Decode(string token, ReadOnlySpan<byte> hmacKey)
    {
        string raw;
        try { raw = Encoding.UTF8.GetString(Convert.FromBase64String(token)); }
        catch { throw new ArgumentException("Invalid cursor token."); }

        // Expected format: v2:{id}:{ticks}:{base64Hmac}
        // base64 of HMAC-SHA256 (32 bytes) = 44 chars, never contains ':'
        var lastColon = raw.LastIndexOf(':');
        if (lastColon < 0)
            throw new ArgumentException("Invalid cursor token format.");

        var hmacBase64 = raw[(lastColon + 1)..];
        var payload    = raw[..lastColon];

        var parts = payload.Split(':');
        if (parts.Length != 3 || parts[0] != "v2")
            throw new ArgumentException("Invalid cursor token format.");

        // Verify HMAC before parsing values (timing-safe comparison)
        byte[] expectedHmac;
        try { expectedHmac = Convert.FromBase64String(hmacBase64); }
        catch { throw new ArgumentException("Invalid cursor token signature."); }

        var actualHmac = HMACSHA256.HashData(hmacKey, Encoding.UTF8.GetBytes(payload));
        if (!CryptographicOperations.FixedTimeEquals(expectedHmac, actualHmac))
            throw new ArgumentException("Invalid cursor token signature.");

        if (!long.TryParse(parts[1], out var id) || !long.TryParse(parts[2], out var ticks))
            throw new ArgumentException("Invalid cursor token values.");

        return (id, ticks);
    }

    /// <summary>
    /// Encodes a cursor where the primary key is a <see cref="string"/> (e.g. node_id).
    /// Format (before outer base64): v2n:{nodeIdBase64}:{ticks}:{base64Hmac}
    /// </summary>
    public static string EncodeString(string id, long ticks, ReadOnlySpan<byte> hmacKey)
    {
        var idBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(id));
        var payload  = $"v2n:{idBase64}:{ticks}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hmac     = HMACSHA256.HashData(hmacKey, payloadBytes);
        var combined = $"{payload}:{Convert.ToBase64String(hmac)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(combined));
    }

    /// <summary>
    /// Decodes and verifies a string-keyed cursor.
    /// Throws <see cref="ArgumentException"/> on any malformed or tampered token.
    /// </summary>
    public static (string Id, long Ticks) DecodeString(string token, ReadOnlySpan<byte> hmacKey)
    {
        string raw;
        try { raw = Encoding.UTF8.GetString(Convert.FromBase64String(token)); }
        catch { throw new ArgumentException("Invalid cursor token."); }

        var lastColon = raw.LastIndexOf(':');
        if (lastColon < 0)
            throw new ArgumentException("Invalid cursor token format.");

        var hmacBase64 = raw[(lastColon + 1)..];
        var payload    = raw[..lastColon];

        var parts = payload.Split(':');
        if (parts.Length != 3 || parts[0] != "v2n")
            throw new ArgumentException("Invalid cursor token format.");

        byte[] expectedHmac;
        try { expectedHmac = Convert.FromBase64String(hmacBase64); }
        catch { throw new ArgumentException("Invalid cursor token signature."); }

        var actualHmac = HMACSHA256.HashData(hmacKey, Encoding.UTF8.GetBytes(payload));
        if (!CryptographicOperations.FixedTimeEquals(expectedHmac, actualHmac))
            throw new ArgumentException("Invalid cursor token signature.");

        if (!long.TryParse(parts[2], out var ticks))
            throw new ArgumentException("Invalid cursor token values.");

        string decodedId;
        try { decodedId = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])); }
        catch { throw new ArgumentException("Invalid cursor token id encoding."); }

        return (decodedId, ticks);
    }
}
