using MSOSync.Sdk.Metadata;

namespace MSOSync.Sdk.Abstractions;

public interface IPluginContext
{
    PluginMetadata       Metadata      { get; }
    IPluginLogger        Logger        { get; }
    IPluginConfiguration Configuration { get; }
    IPluginServices      Services      { get; }
    IPluginEnvironment   Environment   { get; }
}
