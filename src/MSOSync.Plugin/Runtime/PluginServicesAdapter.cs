using Microsoft.Extensions.DependencyInjection;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Runtime;

// Adapter pattern: transparently delegates all service resolution to the host container.
// IServiceScopeFactory is not used because this adapter does not own the scope —
// it surfaces the already-scoped container provided by the plugin host at call time.
internal sealed class PluginServicesAdapter(IServiceProvider provider) : IPluginServices
{
    public T GetRequiredService<T>() where T : notnull
        => provider.GetRequiredService<T>();

    public T? GetService<T>()
        => provider.GetService<T>();

    public IEnumerable<T> GetServices<T>()
        => provider.GetServices<T>();
}
