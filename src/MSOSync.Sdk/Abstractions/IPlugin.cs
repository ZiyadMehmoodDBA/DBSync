namespace MSOSync.Sdk.Abstractions;

public interface IPlugin : IAsyncDisposable
{
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
