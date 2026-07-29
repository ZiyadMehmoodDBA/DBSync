using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Auth;

/// <summary>
/// Manages API keys for local users (msk_ prefix) and service accounts (msa_ prefix).
/// Keys are stored as SHA-256 hashes; the raw key is only returned once at creation time.
/// </summary>
internal sealed class ApiKeyService(AppDbContext db) : IApiKeyService
{
    // ── User API keys ─────────────────────────────────────────────────────────

    public async Task<(string RawKey, SyncUserApiKey Entity)> CreateUserKeyAsync(
        long userId, string name, DateTime? expiresAt = null, CancellationToken ct = default)
    {
        var (rawKey, prefix, hash) = GenerateKey("msk");

        var entity = new SyncUserApiKey
        {
            UserId    = userId,
            KeyPrefix = prefix,
            KeyHash   = hash,
            Name      = name,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };

        db.UserApiKeys.Add(entity);
        await db.SaveChangesAsync(ct);
        return (rawKey, entity);
    }

    public async Task<SyncUser?> ValidateUserKeyAsync(
        string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 8) return null;

        var prefix = apiKey[..8];
        var hash   = HashKey(apiKey);

        var key = await db.UserApiKeys
            .Include(k => k.User)
            .FirstOrDefaultAsync(k =>
                k.KeyPrefix == prefix
                && k.KeyHash == hash
                && !k.IsRevoked
                && (k.ExpiresAt == null || k.ExpiresAt > DateTime.UtcNow),
                ct);

        if (key is null) return null;

        key.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return key.User;
    }

    public async Task RevokeUserKeyAsync(int keyId, CancellationToken ct = default)
    {
        var key = await db.UserApiKeys.FindAsync([keyId], ct);
        if (key is null) return;
        key.IsRevoked = true;
        key.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ── Service accounts ──────────────────────────────────────────────────────

    public async Task<(string RawKey, SyncServiceAccount Entity)> CreateServiceAccountAsync(
        string name, string[] permissions, CancellationToken ct = default)
    {
        // ClientId is the public identifier (UUID); the raw key = "msa_" + secret payload.
        // We store the clientId in ClientId and the SHA-256 hash of the full raw key in
        // ClientSecretHash. Permissions are serialised to JSON in Description.
        var clientId = Guid.NewGuid().ToString("N"); // 32 hex chars, no dashes
        var (rawKey, _, hash) = GenerateKey("msa");

        var entity = new SyncServiceAccount
        {
            Name             = name,
            ClientId         = clientId,
            ClientSecretHash = hash,
            Description      = JsonSerializer.Serialize(permissions),
            CreatedAt        = DateTime.UtcNow,
            IsEnabled        = true,
        };

        db.ServiceAccounts.Add(entity);
        await db.SaveChangesAsync(ct);
        return (rawKey, entity);
    }

    public async Task<SyncServiceAccount?> ValidateServiceAccountKeyAsync(
        string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(apiKey)) return null;

        var hash = HashKey(apiKey);

        return await db.ServiceAccounts
            .FirstOrDefaultAsync(a =>
                a.ClientSecretHash == hash
                && !a.IsRevoked
                && a.IsEnabled
                && (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow),
                ct);
    }

    public async Task RevokeServiceAccountAsync(int id, CancellationToken ct = default)
    {
        var account = await db.ServiceAccounts.FindAsync([id], ct);
        if (account is null) return;
        account.IsRevoked = true;
        account.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ── Key generation ────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a raw key with format: {prefix}_{8-char-id}_{32-char-secret}
    /// Returns (rawKey, 8-char display prefix, SHA-256 hash).
    /// </summary>
    private static (string RawKey, string Prefix, string Hash) GenerateKey(string prefixTag)
    {
        var idPart     = GenerateAlphanumeric(8);
        var secretPart = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
                            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var rawKey = $"{prefixTag}_{idPart}_{secretPart}";
        // KeyPrefix stored in DB: first 8 chars of rawKey (e.g. "msk_ab12")
        var prefix = rawKey[..8];
        return (rawKey, prefix, HashKey(rawKey));
    }

    private static string GenerateAlphanumeric(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    private static string HashKey(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
