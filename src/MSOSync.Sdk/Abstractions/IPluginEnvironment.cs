namespace MSOSync.Sdk.Abstractions;

public interface IPluginEnvironment
{
    string EnvironmentName { get; }
    bool   IsDevelopment   { get; }
    bool   IsProduction    { get; }
    string HostVersion     { get; }
    string DataDirectory   { get; }
    string PluginDirectory { get; }
}
