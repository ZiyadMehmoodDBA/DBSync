namespace ConfigDrivenPlugin;

internal sealed record PluginSettings(
    bool EnableDetailedLogging,
    int MaxBatchSize,
    int RetryMaxAttempts,
    int RetryDelayMs,
    int WarnAtQueueDepth,
    int ErrorAtQueueDepth);
