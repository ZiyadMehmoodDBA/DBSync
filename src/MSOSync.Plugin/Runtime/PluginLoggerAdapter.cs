using Microsoft.Extensions.Logging;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Runtime;

internal sealed class PluginLoggerAdapter(ILogger logger) : IPluginLogger
{
    public void LogDebug(string message, params object?[] args)
        => logger.LogDebug(message, args);

    public void LogInformation(string message, params object?[] args)
        => logger.LogInformation(message, args);

    public void LogWarning(string message, params object?[] args)
        => logger.LogWarning(message, args);

    public void LogWarning(Exception exception, string message, params object?[] args)
        => logger.LogWarning(exception, message, args);

    public void LogError(Exception? exception, string message, params object?[] args)
        => logger.LogError(exception, message, args);

    public void LogCritical(Exception? exception, string message, params object?[] args)
        => logger.LogCritical(exception, message, args);

    public IDisposable BeginScope(string name)
        => logger.BeginScope(name) ?? NullScope.Instance;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
