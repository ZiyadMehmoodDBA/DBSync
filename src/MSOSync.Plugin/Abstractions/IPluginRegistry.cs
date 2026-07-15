using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Abstractions;

public interface IPluginRegistry
{
    bool IsInitialized { get; }
    IReadOnlyList<PluginDescriptor> GetAll();
    PluginDescriptor? GetById(string pluginId);
    void Register(PluginDescriptor descriptor);
    void UpdateStatus(string pluginId, PluginStatus status, string? error = null);
    void MarkInitialized();
}
