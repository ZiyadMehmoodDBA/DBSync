namespace MSOSync.Sdk.Abstractions;

public interface IPluginLogger
{
    void        LogDebug(string message, params object?[] args);
    void        LogInformation(string message, params object?[] args);
    void        LogWarning(string message, params object?[] args);
    void        LogWarning(Exception exception, string message, params object?[] args);
    void        LogError(Exception? exception, string message, params object?[] args);
    void        LogCritical(Exception? exception, string message, params object?[] args);
    IDisposable BeginScope(string name);
}
