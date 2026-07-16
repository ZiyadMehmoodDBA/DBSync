using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Lifecycle;

internal interface ISdkCompatibilityValidator
{
    CompatibilityResult Validate(PluginManifest manifest, out string? message);
}
