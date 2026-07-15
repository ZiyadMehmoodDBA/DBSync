namespace MSOSync.Plugin.Abstractions;

public interface IPluginHost
{
    bool IsStarted { get; }
    DateTime? StartedAt { get; }
    long StartupDurationMs { get; }
}
