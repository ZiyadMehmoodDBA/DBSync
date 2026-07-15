using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Metadata;

namespace MSOSync.Plugin.Runtime;

internal sealed class PluginContext(
    PluginMetadata       metadata,
    IPluginLogger        logger,
    IPluginConfiguration configuration,
    IPluginServices      services,
    IPluginEnvironment   environment) : IPluginContext
{
    public PluginMetadata       Metadata      { get; } = metadata;
    public IPluginLogger        Logger        { get; } = logger;
    public IPluginConfiguration Configuration { get; } = configuration;
    public IPluginServices      Services      { get; } = services;
    public IPluginEnvironment   Environment   { get; } = environment;
}
