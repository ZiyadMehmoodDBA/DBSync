namespace MSOSync.Plugin.Hosting;

public interface IPluginRuntimeManager
{
    long LoadAndActivateElapsedMs { get; }
    long InitializeElapsedMs { get; }
    long StartElapsedMs      { get; }
    long TotalElapsedMs      { get; }
    Task LoadAndActivateAsync(CancellationToken ct);
    Task InitializeAsync(CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task DisposeAsync(CancellationToken ct);
}
