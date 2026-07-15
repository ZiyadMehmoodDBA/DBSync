using System.Runtime.Loader;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Abstractions;

public interface IPluginLoader
{
    IReadOnlyList<AssemblyLoadContext> LoadContexts { get; }
    Task<IReadOnlyList<PluginLoadResult>> LoadAllAsync(string pluginsPath, CancellationToken ct);
}
