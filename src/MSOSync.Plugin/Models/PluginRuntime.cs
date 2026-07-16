using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Models;

// Changed from 'sealed record' to 'sealed class' — record equality semantics are wrong
// for a mutable runtime object. Properties use 'set' throughout.
internal sealed class PluginRuntime
{
    public PluginDescriptor      Descriptor     { get; set; } = null!;
    public Assembly?             Assembly       { get; set; }
    public AssemblyLoadContext?  LoadContext    { get; set; }

    // 14B runtime fields
    public IPlugin?              Instance       { get; set; }
    public IServiceProvider?     PluginServices { get; set; }
    public IPluginContext?       Context        { get; set; }
    public PluginRuntimeState    State          { get; set; } = PluginRuntimeState.Loaded;
    public Exception?            LastException  { get; set; }

    // Lifecycle timestamps
    public DateTime? InitializedAt      { get; set; }
    public DateTime? StartedAt          { get; set; }
    public DateTime? StoppedAt          { get; set; }
    public DateTime? DisposedAt         { get; set; }
    public DateTime  LastStateChangeUtc { get; set; }

    // Lifecycle durations
    public TimeSpan? InitializeDuration { get; set; }
    public TimeSpan? StartDuration      { get; set; }
    public TimeSpan? StopDuration       { get; set; }
    public TimeSpan? DisposeDuration    { get; set; }
    public TimeSpan? TotalDuration      { get; set; }
}
