using MSOSync.Sdk.Hosting;

namespace MyPlugin;

public sealed class MyPlugin : PluginBase
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Context.Logger.LogInformation(
            "{PluginId} started (host: {HostVersion})",
            Context.Metadata.PluginId,
            Context.Environment.HostVersion);

        return Task.CompletedTask;
    }
}
