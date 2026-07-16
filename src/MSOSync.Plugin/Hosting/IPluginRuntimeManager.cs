namespace MSOSync.Plugin.Hosting;

public interface IPluginRuntimeManager
{
    long LoadElapsedMs       { get; }
    long InitializeElapsedMs { get; }
    long StartElapsedMs      { get; }
    Task LoadAndActivateAsync(CancellationToken ct);
    Task InitializeAsync(CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task DisposeAsync(CancellationToken ct);
}
