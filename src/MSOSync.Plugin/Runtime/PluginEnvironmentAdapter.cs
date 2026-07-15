using Microsoft.Extensions.Hosting;
using MSOSync.Plugin.Models;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Runtime;

internal sealed class PluginEnvironmentAdapter(
    IHostEnvironment   hostEnv,
    PluginHostOptions  options,
    string             pluginDirectory) : IPluginEnvironment
{
    public string EnvironmentName => hostEnv.EnvironmentName;
    public bool   IsDevelopment   => hostEnv.IsDevelopment();
    public bool   IsProduction    => hostEnv.IsProduction();
    public string HostVersion     => options.HostVersion;
    public string DataDirectory   => hostEnv.ContentRootPath;
    public string PluginDirectory => pluginDirectory;
}
