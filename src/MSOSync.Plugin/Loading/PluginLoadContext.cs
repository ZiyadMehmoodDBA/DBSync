using System.Reflection;
using System.Runtime.Loader;

namespace MSOSync.Plugin.Loading;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string? _libDirectory;

    public PluginLoadContext(string pluginDirectory, string? libDirectory = null)
        : base(isCollectible: true)
    {
        // Primary resolver targets the plugin's main directory
        _resolver   = new AssemblyDependencyResolver(pluginDirectory);
        _libDirectory = libDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 1. Try plugin main directory
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path != null)
            return LoadFromAssemblyPath(path);

        // 2. Try lib/ subdirectory
        if (_libDirectory != null)
        {
            var libPath = Path.Combine(_libDirectory, $"{assemblyName.Name}.dll");
            if (File.Exists(libPath))
                return LoadFromAssemblyPath(libPath);
        }

        // 3. Fall back to host/shared context
        return null;
    }
}
