using System.Reflection;
using System.Runtime.Loader;

namespace MSOSync.Plugin.Models;

// Internal to MSOSync.Plugin. Never exposed via API.
// Assembly and LoadContext are null in 14A (populated in 14B when plugin activation is added).
internal sealed record PluginRuntime
{
    public PluginDescriptor     Descriptor   { get; set; } = null!;
    public Assembly?            Assembly     { get; init; }
    public AssemblyLoadContext? LoadContext  { get; init; }
}
