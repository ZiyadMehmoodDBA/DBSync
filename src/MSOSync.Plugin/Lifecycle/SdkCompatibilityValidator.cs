using Microsoft.Extensions.Options;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Lifecycle;

public sealed class SdkCompatibilityValidator(IOptions<PluginHostOptions> options) : ISdkCompatibilityValidator
{
    public CompatibilityResult Validate(PluginManifest manifest, out string? message)
    {
        message = null;

        // SdkVersion already validated in PluginManifestValidator — parse is safe here
        if (!Version.TryParse(manifest.SdkVersion, out var pluginSdk))
        {
            message = $"Cannot parse sdkVersion '{manifest.SdkVersion}'";
            return CompatibilityResult.Incompatible;
        }

        if (!int.TryParse(options.Value.SupportedSdkMajorVersion, out var supportedMajor))
            supportedMajor = 1;

        if (pluginSdk.Major != supportedMajor)
        {
            message = $"Plugin sdkVersion major={pluginSdk.Major} is not compatible with host sdkMajor={supportedMajor}";
            return CompatibilityResult.Incompatible;
        }

        // ApiVersion check
        if (!int.TryParse(manifest.ApiVersion, out var pluginApi))
        {
            message = $"Cannot parse apiVersion '{manifest.ApiVersion}'";
            return CompatibilityResult.Incompatible;
        }

        if (!int.TryParse(options.Value.SupportedApiVersion, out var supportedApi))
            supportedApi = 1;

        if (pluginApi != supportedApi)
        {
            message = $"Plugin apiVersion={pluginApi} does not match host apiVersion={supportedApi}";
            return CompatibilityResult.Incompatible;
        }

        return CompatibilityResult.Compatible;
    }
}
