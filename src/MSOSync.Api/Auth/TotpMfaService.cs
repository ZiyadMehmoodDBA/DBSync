using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Auth;

/// <summary>
/// TOTP MFA service — RFC 6238 implementation using System.Security.Cryptography only.
/// No external OTP package required.
/// </summary>
internal sealed class TotpMfaService(AppDbContext db) : IMfaService
{
    private const int StepSeconds   = 30;
    private const int TotpDigits    = 6;
    private const int BackupCodeCount = 8;

    // ── Enrol ──────────────────────────────────────────────────────────────

    public async Task<string> EnrollAsync(long userId, CancellationToken ct = default)
    {
        var existing = await db.TotpSecrets.FindAsync([userId], ct);
        if (existing is not null)
        {
            // Re-enrol: reset the secret and disable until re-confirmed.
            var newKey = RandomNumberGenerator.GetBytes(20); // 160-bit
            existing.Secret    = ToBase32(newKey);
            existing.IsEnabled = false;
            existing.EnabledAt = null;
            await db.SaveChangesAsync(ct);
            return existing.Secret;
        }

        var keyBytes = RandomNumberGenerator.GetBytes(20);
        var secret   = ToBase32(keyBytes);
        db.TotpSecrets.Add(new SyncUserTotpSecret
        {
            UserId  = userId,
            Secret  = secret,
            IsEnabled = false,
        });
        await db.SaveChangesAsync(ct);
        return secret;
    }

    // ── Confirm enrolment ─────────────────────────────────────────────────

    public async Task ConfirmEnrollmentAsync(long userId, string code, CancellationToken ct = default)
    {
        var record = await db.TotpSecrets.FindAsync([userId], ct)
            ?? throw new InvalidOperationException($"No TOTP enrollment found for user {userId}.");

        if (!VerifyTotp(record.Secret, code))
            throw new InvalidOperationException("Invalid or expired TOTP code.");

        record.IsEnabled = true;
        record.EnabledAt = DateTime.UtcNow;

        // Update user's IsMfaEnabled flag via tracked entity (ExecuteUpdateAsync not
        // supported by InMemory provider used in unit tests).
        var user = await db.Users.FindAsync([userId], ct);
        if (user is not null)
            user.IsMfaEnabled = true;

        await db.SaveChangesAsync(ct);
    }

    // ── Backup codes ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<string>> GenerateBackupCodesAsync(long userId, CancellationToken ct = default)
    {
        // Invalidate old codes (use tracked removal for InMemory provider compatibility)
        var old = await db.BackupCodes
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);
        db.BackupCodes.RemoveRange(old);

        var rawCodes = new string[BackupCodeCount];
        for (var i = 0; i < BackupCodeCount; i++)
        {
            rawCodes[i] = GenerateBackupCode();
            db.BackupCodes.Add(new SyncUserBackupCode
            {
                UserId   = userId,
                CodeHash = HashBackupCode(rawCodes[i]),
            });
        }

        await db.SaveChangesAsync(ct);
        return rawCodes;
    }

    // ── Queries ───────────────────────────────────────────────────────────

    public async Task<bool> IsEnabledAsync(long userId, CancellationToken ct = default)
    {
        var record = await db.TotpSecrets.FindAsync([userId], ct);
        return record?.IsEnabled == true;
    }

    // ── Verify TOTP ───────────────────────────────────────────────────────

    public async Task<bool> VerifyTotpAsync(long userId, string code, CancellationToken ct = default)
    {
        var record = await db.TotpSecrets.FindAsync([userId], ct);
        if (record is null || !record.IsEnabled) return false;
        return VerifyTotp(record.Secret, code);
    }

    // ── Verify backup code ────────────────────────────────────────────────

    public async Task<bool> VerifyBackupCodeAsync(long userId, string code, CancellationToken ct = default)
    {
        var hash   = HashBackupCode(code);
        var backup = await db.BackupCodes
            .FirstOrDefaultAsync(c => c.UserId == userId && c.CodeHash == hash && !c.IsUsed, ct);

        if (backup is null) return false;

        backup.IsUsed = true;
        backup.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── RFC 6238 TOTP ─────────────────────────────────────────────────────

    /// <summary>
    /// Verifies a 6-digit TOTP code with ±1 step (30 s) tolerance.
    /// </summary>
    private static bool VerifyTotp(string base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != TotpDigits) return false;

        var keyBytes  = FromBase32(base32Secret);
        var unixNow   = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var stepNow   = unixNow / StepSeconds;

        // Check current step and ±1 step for clock drift tolerance
        for (var delta = -1; delta <= 1; delta++)
        {
            if (ComputeTotp(keyBytes, stepNow + delta) == code)
                return true;
        }

        return false;
    }

    /// <summary>Computes HOTP (RFC 4226) for the given counter, then applies TOTP (RFC 6238) truncation.</summary>
    internal static string ComputeTotp(byte[] keyBytes, long counter)
    {
        // Counter as big-endian 8-byte array
        var counterBytes = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        // HMAC-SHA1
        using var hmac = new HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(counterBytes);

        // Dynamic truncation (RFC 4226 §5.3)
        var offset = hash[^1] & 0x0F;
        var binCode =
            ((hash[offset]     & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) <<  8) |
             (hash[offset + 3] & 0xFF);

        var otp = binCode % (int)Math.Pow(10, TotpDigits);
        return otp.ToString().PadLeft(TotpDigits, '0');
    }

    // ── Base32 (RFC 4648) ─────────────────────────────────────────────────

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    internal static string ToBase32(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;

        var sb       = new StringBuilder(((data.Length * 8) + 4) / 5);
        var buffer   = 0;
        var bitsLeft = 0;

        foreach (var b in data)
        {
            buffer    = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(Base32Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        if (bitsLeft > 0)
            sb.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);

        return sb.ToString();
    }

    internal static byte[] FromBase32(string base32)
    {
        if (string.IsNullOrEmpty(base32)) return [];

        var upper    = base32.TrimEnd('=').ToUpperInvariant();
        var result   = new byte[upper.Length * 5 / 8];
        var buffer   = 0;
        var bitsLeft = 0;
        var idx      = 0;

        foreach (var c in upper)
        {
            var val = Base32Alphabet.IndexOf(c);
            if (val < 0)
                throw new FormatException($"Invalid Base32 character: '{c}'");

            buffer    = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                result[idx++] = (byte)((buffer >> bitsLeft) & 0xFF);
            }
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string GenerateBackupCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashBackupCode(string rawCode)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawCode.ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
