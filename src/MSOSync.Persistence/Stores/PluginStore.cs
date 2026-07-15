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
                PluginId      = p.PluginId,
                PluginName    = p.PluginName,
                PluginVersion = p.PluginVersion,
                Status        = p.Status,
                Enabled       = p.Enabled,
                InstalledAt   = p.InstalledAt,
                LastSeenAt    = p.LastSeenAt,
                LastError     = p.LastError,
                ManifestHash  = p.ManifestHash,
                HostVersion   = p.HostVersion,
            })
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(PluginRecord record, CancellationToken ct)
    {
        var existing = await db.Plugins
            .FirstOrDefaultAsync(p => p.PluginId == record.PluginId, ct);

        if (existing == null)
        {
            db.Plugins.Add(new Entities.SyncPlugin
            {
                PluginId      = record.PluginId,
                PluginName    = record.PluginName,
                PluginVersion = record.PluginVersion,
                Status        = record.Status,
                Enabled       = record.Enabled,
                InstalledAt   = record.InstalledAt,
                LastSeenAt    = record.LastSeenAt,
                LastError     = record.LastError,
                ManifestHash  = record.ManifestHash,
                HostVersion   = record.HostVersion,
            });
        }
        else
        {
            existing.PluginName    = record.PluginName;
            existing.PluginVersion = record.PluginVersion;
            existing.Status        = record.Status;
            existing.LastSeenAt    = record.LastSeenAt;
            existing.LastError     = record.LastError;
            existing.ManifestHash  = record.ManifestHash;
            existing.HostVersion   = record.HostVersion;
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
