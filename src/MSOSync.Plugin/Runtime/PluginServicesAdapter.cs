using Microsoft.Extensions.DependencyInjection;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Runtime;

internal sealed class PluginServicesAdapter(IServiceProvider provider) : IPluginServices
{
    public T GetRequiredService<T>() where T : notnull
        => provider.GetRequiredService<T>();

    public T? GetService<T>()
        => provider.GetService<T>();

    public IEnumerable<T> GetServices<T>()
        => provider.GetServices<T>();
}
