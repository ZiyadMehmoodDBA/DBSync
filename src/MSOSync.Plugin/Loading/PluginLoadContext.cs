using System.Reflection;
using System.Runtime.Loader;

namespace MSOSync.Plugin.Loading;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string? _libDirectory;

    public PluginLoadContext(string componentDllPath, string? libDirectory = null)
        : base(isCollectible: true)
    {
        _resolver     = new AssemblyDependencyResolver(componentDllPath);
        _libDirectory = libDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 1. Try resolver (uses deps.json from the component DLL path)
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
