namespace MSOSync.Plugin.Packaging;

public sealed class PluginPackagingException : Exception
{
    public string Stage { get; }

    public PluginPackagingException(string stage, string message)
        : base($"[{stage}] {message}")
        => Stage = stage;

    public PluginPackagingException(string stage, string message, Exception inner)
        : base($"[{stage}] {message}", inner)
        => Stage = stage;
}
