using MSOSync.Sdk.Hosting;

namespace HelloWorldPlugin;

public sealed class HelloWorldPlugin : PluginBase
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("HelloWorldPlugin.Start");

        Context.Logger.LogInformation(
            "Hello World from {PluginId} v{Version} (host: {HostVersion}, env: {Env})",
            Context.Metadata.PluginId,
            Context.Metadata.Version,
            Context.Environment.HostVersion,
            Context.Environment.EnvironmentName);

        if (Context.Environment.IsDevelopment)
        {
            Context.Logger.LogDebug(
                "Plugin directory: {PluginDir}, Data directory: {DataDir}",
                Context.Environment.PluginDirectory,
                Context.Environment.DataDirectory);
        }

        return Task.CompletedTask;
    }
}
