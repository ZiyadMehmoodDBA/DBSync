using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Lifecycle;

public interface ISdkCompatibilityValidator
{
    CompatibilityResult Validate(PluginManifest manifest, out string? message);
}
