using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace MyPlugin;

public sealed class MyPlugin : PluginBase
{
    private Timer? _workTimer;
    private MyPluginSettings? _settings;

    public override async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);

        // Load and validate settings at initialization
        _settings = LoadSettings();

        Context.Logger.LogInformation(
            "Initializing {PluginId}",
            Context.Metadata.PluginId);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("MyPlugin.Start");

        Context.Logger.LogInformation(
            "Starting {PluginId} (host: {HostVersion})",
            Context.Metadata.PluginId,
            Context.Environment.HostVersion);

        // Start background work timer (optional)
        _workTimer = new Timer(
            _ => DoWork(),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30));

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("MyPlugin.Stop");
        Context.Logger.LogInformation("Stopping {PluginId}", Context.Metadata.PluginId);
        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        _workTimer?.Dispose();
        await base.DisposeAsync();
    }

    private MyPluginSettings LoadSettings()
    {
        var configSection = Context.Configuration.GetSection("Config");
        return new MyPluginSettings(
            Enabled: configSection.GetValue("Enabled", true),
            IntervalSeconds: configSection.GetValue("IntervalSeconds", 30));
    }

    private void DoWork()
    {
        try
        {
            Context.Logger.LogDebug("Performing work...");

            // Add plugin logic here
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "Error during work");
        }
    }
}
