using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Models;

// Internal to MSOSync.Plugin. Never exposed via API.
internal sealed record PluginRuntime
{
    public PluginDescriptor     Descriptor     { get; set; } = null!;
    public Assembly?            Assembly       { get; set; }
    public AssemblyLoadContext? LoadContext    { get; set; }
    public IPlugin?             Instance       { get; set; }
    public IServiceProvider?    PluginServices { get; set; }
    public IPluginContext?      Context        { get; set; }
}
