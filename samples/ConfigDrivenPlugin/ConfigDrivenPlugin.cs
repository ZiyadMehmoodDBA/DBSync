using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace ConfigDrivenPlugin;

public sealed class ConfigDrivenPlugin : PluginBase
{
    private Timer? _hotReloadTimer;
    private PluginSettings? _cachedSettings;

    public override async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);

        // Log all resolved keys at startup
        if (Context.Environment.IsDevelopment)
        {
            Context.Logger.LogInformation(
                "Configuration keys resolved at startup: {Count}",
                Context.Configuration.Keys.Count);

            foreach (var key in Context.Configuration.Keys)
            {
                Context.Logger.LogDebug("  - {Key}", key);
            }
        }
        else
        {
            Context.Logger.LogInformation(
                "Configuration keys resolved: {Count}",
                Context.Configuration.Keys.Count);
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("ConfigDrivenPlugin.Start");

        // Load initial settings
        _cachedSettings = LoadSettings();

        Context.Logger.LogInformation(
            "Config-driven plugin started (PluginId: {PluginId}, DetailedLogging: {DetailedLogging})",
            Context.Metadata.PluginId,
            _cachedSettings.EnableDetailedLogging);

        // Start hot-reload timer (check config every 30 seconds)
        _hotReloadTimer = new Timer(
            _ => CheckForConfigChanges(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("ConfigDrivenPlugin.Stop");
        Context.Logger.LogInformation("Config-driven plugin stopping");
        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        _hotReloadTimer?.Dispose();
        await base.DisposeAsync();
    }

    private PluginSettings LoadSettings()
    {
        var featureSection = Context.Configuration.GetSection("Feature");
        var retrySection = Context.Configuration.GetSection("Retry");
        var thresholdSection = Context.Configuration.GetSection("Thresholds");

        return new PluginSettings(
            EnableDetailedLogging: featureSection.GetValue("EnableDetailedLogging", false),
            MaxBatchSize: featureSection.GetValue("MaxBatchSize", 100),
            RetryMaxAttempts: retrySection.GetValue("MaxAttempts", 3),
            RetryDelayMs: retrySection.GetValue("DelayMs", 1000),
            WarnAtQueueDepth: thresholdSection.GetValue("WarnAtQueueDepth", 1000),
            ErrorAtQueueDepth: thresholdSection.GetValue("ErrorAtQueueDepth", 5000));
    }

    private void CheckForConfigChanges()
    {
        try
        {
            var newSettings = LoadSettings();

            if (_cachedSettings == null)
            {
                return;
            }

            // Check for changes and log them
            if (newSettings.EnableDetailedLogging != _cachedSettings.EnableDetailedLogging)
            {
                Context.Logger.LogInformation(
                    "Config changed: EnableDetailedLogging {Old} -> {New}",
                    _cachedSettings.EnableDetailedLogging,
                    newSettings.EnableDetailedLogging);
            }

            if (newSettings.MaxBatchSize != _cachedSettings.MaxBatchSize)
            {
                Context.Logger.LogInformation(
                    "Config changed: MaxBatchSize {Old} -> {New}",
                    _cachedSettings.MaxBatchSize,
                    newSettings.MaxBatchSize);
            }

            if (newSettings.RetryMaxAttempts != _cachedSettings.RetryMaxAttempts)
            {
                Context.Logger.LogInformation(
                    "Config changed: RetryMaxAttempts {Old} -> {New}",
                    _cachedSettings.RetryMaxAttempts,
                    newSettings.RetryMaxAttempts);
            }

            // Update cached settings
            _cachedSettings = newSettings;
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "Error checking for configuration changes");
        }
    }
}
