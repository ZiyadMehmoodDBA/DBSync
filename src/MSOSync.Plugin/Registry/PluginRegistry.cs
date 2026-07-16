using System.Collections.Concurrent;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Registry;

public sealed class PluginRegistry : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, PluginRuntime> _runtimes =
        new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _initialized;

    public bool IsInitialized => _initialized;

    public IReadOnlyList<PluginDescriptor> GetAll()
        => _runtimes.Values.Select(r => r.Descriptor).ToList();

    public PluginDescriptor? GetById(string pluginId)
        => _runtimes.TryGetValue(pluginId, out var rt) ? rt.Descriptor : null;

    public void Register(PluginDescriptor descriptor)
    {
        var runtime = new PluginRuntime { Descriptor = descriptor };
        _runtimes[descriptor.PluginId] = runtime;
    }

    public void UpdateStatus(string pluginId, PluginStatus status, string? error = null)
    {
        if (_runtimes.TryGetValue(pluginId, out var rt))
        {
            rt.Descriptor.Status       = status;
            rt.Descriptor.ErrorMessage = error;
        }
    }

    public void MarkInitialized() => _initialized = true;

    internal PluginRuntime? GetRuntime(string pluginId)
        => _runtimes.TryGetValue(pluginId, out var rt) ? rt : null;

    internal IReadOnlyList<PluginRuntime> GetAllRuntimes()
        => _runtimes.Values.ToList();
}
