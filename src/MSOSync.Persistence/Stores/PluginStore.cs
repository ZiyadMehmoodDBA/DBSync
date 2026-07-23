using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Persistence.Stores;

public sealed class PluginStore(AppDbContext db) : IPluginStore
{
    public async Task<IReadOnlyList<PluginRecord>> GetAllAsync(CancellationToken ct)
    {
        return await db.Plugins
            .AsNoTracking()
            .Select(p => new PluginRecord
            {
                PluginId           = p.PluginId,
                PluginName         = p.PluginName,
                PluginVersion      = p.PluginVersion,
                Status             = p.Status,
                Enabled            = p.Enabled,
                InstalledAt        = p.InstalledAt,
                LastSeenAt         = p.LastSeenAt,
                LastError          = p.LastError,
                ManifestHash       = p.ManifestHash,
                HostVersion        = p.HostVersion,
                PackageHash        = p.PackageHash,
                SignedBy           = p.SignedBy,
                SignatureAlgorithm = p.SignatureAlgorithm,
                IsPackageInstall   = p.IsPackageInstall,
            })
            .ToListAsync(ct);
    }

    public async Task<PluginRecord?> GetByIdAsync(string pluginId, CancellationToken ct)
    {
        var entity = await db.Plugins
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PluginId == pluginId, ct);

        return entity is null ? null : new PluginRecord
        {
            PluginId           = entity.PluginId,
            PluginName         = entity.PluginName,
            PluginVersion      = entity.PluginVersion,
            Status             = entity.Status,
            Enabled            = entity.Enabled,
            InstalledAt        = entity.InstalledAt,
            LastSeenAt         = entity.LastSeenAt,
            LastError          = entity.LastError,
            ManifestHash       = entity.ManifestHash,
            HostVersion        = entity.HostVersion,
            PackageHash        = entity.PackageHash,
            SignedBy           = entity.SignedBy,
            SignatureAlgorithm = entity.SignatureAlgorithm,
            IsPackageInstall   = entity.IsPackageInstall,
        };
    }

    public async Task DeleteAsync(string pluginId, CancellationToken ct)
    {
        var entity = await db.Plugins
            .FirstOrDefaultAsync(p => p.PluginId == pluginId, ct);

        if (entity is not null)
        {
            db.Plugins.Remove(entity);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UpsertAsync(PluginRecord record, CancellationToken ct)
    {
        var existing = await db.Plugins
            .FirstOrDefaultAsync(p => p.PluginId == record.PluginId, ct);

        if (existing == null)
        {
            db.Plugins.Add(new Entities.SyncPlugin
            {
                PluginId           = record.PluginId,
                PluginName         = record.PluginName,
                PluginVersion      = record.PluginVersion,
                Status             = record.Status,
                Enabled            = record.Enabled,
                InstalledAt        = record.InstalledAt,
                LastSeenAt         = record.LastSeenAt,
                LastError          = record.LastError,
                ManifestHash       = record.ManifestHash,
                HostVersion        = record.HostVersion,
                PackageHash        = record.PackageHash,
                SignedBy           = record.SignedBy,
                SignatureAlgorithm = record.SignatureAlgorithm,
                IsPackageInstall   = record.IsPackageInstall,
            });
        }
        else
        {
            existing.PluginName         = record.PluginName;
            existing.PluginVersion      = record.PluginVersion;
            existing.Status             = record.Status;
            existing.LastSeenAt         = record.LastSeenAt;
            existing.LastError          = record.LastError;
            existing.ManifestHash       = record.ManifestHash;
            existing.HostVersion        = record.HostVersion;
            existing.PackageHash        = record.PackageHash;
            existing.SignedBy           = record.SignedBy;
            existing.SignatureAlgorithm = record.SignatureAlgorithm;
            existing.IsPackageInstall   = record.IsPackageInstall;
            // Preserve InstalledAt and Enabled — not overwritten by loader
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task TouchAsync(string pluginId, CancellationToken ct)
    {
        await db.Plugins
            .Where(p => p.PluginId == pluginId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.LastSeenAt, DateTime.UtcNow)
                .SetProperty(p => p.LastError,  (string?)null), ct);
    }

    public async Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct)
    {
        var affected = await db.Plugins
            .Where(p => p.PluginId == pluginId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Enabled, enabled), ct);

        if (affected == 0)
            throw new NotFoundException($"Plugin '{pluginId}' not found.");
    }
}
