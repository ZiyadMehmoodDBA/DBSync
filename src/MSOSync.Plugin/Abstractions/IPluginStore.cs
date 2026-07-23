using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Abstractions;

public interface IPluginStore
{
    Task<IReadOnlyList<PluginRecord>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(PluginRecord record, CancellationToken ct);
    Task TouchAsync(string pluginId, CancellationToken ct);
    Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct);
    // 2C.1 packaging additions:
    Task<PluginRecord?> GetByIdAsync(string pluginId, CancellationToken ct);
    Task DeleteAsync(string pluginId, CancellationToken ct);
}
