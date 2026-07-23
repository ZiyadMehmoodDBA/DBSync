using MSOSync.Plugin.Signing.Abstractions;

namespace MSOSync.Plugin.Packaging.Abstractions;

/// <summary>
/// Builds a .msopkg archive from a plugin project output directory.
/// </summary>
public interface IPluginPackager
{
    /// <summary>
    /// Create a .msopkg archive at <paramref name="outputPackagePath"/> from the files in
    /// <paramref name="pluginSourceDirectory"/>.
    /// </summary>
    /// <param name="pluginSourceDirectory">
    ///   Directory containing plugin.json or manifest.json (ManifestV2), the entry DLL,
    ///   optional lib/, and optional assets/. Must exist.
    /// </param>
    /// <param name="outputPackagePath">
    ///   Full path where the .msopkg file will be written. Parent directory must exist.
    /// </param>
    /// <param name="signingKey">
    ///   If provided, the resulting archive is signed. The manifest.json inside the archive
    ///   will include a populated signature block.
    ///   Pass null to produce an unsigned package (valid for local dev when
    ///   PluginSecurityOptions.RequireSignedPackages = false).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="PluginPackagingException">Thrown for any validation or IO failure.</exception>
    Task PackageAsync(
        string         pluginSourceDirectory,
        string         outputPackagePath,
        IPluginSigner? signingKey,
        CancellationToken ct);
}
