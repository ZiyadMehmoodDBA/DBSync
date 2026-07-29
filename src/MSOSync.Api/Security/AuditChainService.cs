using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Security;

internal sealed class AuditChainService(AppDbContext db) : IAuditChainService
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };

    public string ComputeHash(string? prevHash, SyncAudit entry)
    {
        var canonical = $"{prevHash ?? string.Empty}\n{CanonicalEntry(entry)}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task SetHashesAsync(SyncAudit entry, CancellationToken ct = default)
    {
        var prevHash = await db.Audits
            .AsNoTracking()
            .OrderByDescending(e => e.AuditId)
            .Select(e => e.EntryHash)
            .FirstOrDefaultAsync(ct);

        entry.PrevHash  = prevHash;
        entry.EntryHash = ComputeHash(prevHash, entry);
    }

    public async Task<(bool IsValid, long? FirstBrokenId)> VerifyChainAsync(CancellationToken ct = default)
    {
        var entries = await db.Audits
            .AsNoTracking()
            .OrderBy(e => e.AuditId)
            .ToListAsync(ct);

        string? expectedPrevHash = null;
        foreach (var entry in entries)
        {
            if (entry.PrevHash != expectedPrevHash)
                return (false, entry.AuditId);

            var expectedHash = ComputeHash(entry.PrevHash, entry);
            if (entry.EntryHash != expectedHash)
                return (false, entry.AuditId);

            expectedPrevHash = entry.EntryHash;
        }

        return (true, null);
    }

    private static string CanonicalEntry(SyncAudit e) => JsonSerializer.Serialize(new
    {
        audit_id    = e.AuditId,
        username    = e.Username,
        action_name = e.ActionName,
        object_name = e.ObjectName,
        create_time = e.CreateTime?.ToString("O"),
        tenant_id   = e.TenantId,
    }, _opts);
}
