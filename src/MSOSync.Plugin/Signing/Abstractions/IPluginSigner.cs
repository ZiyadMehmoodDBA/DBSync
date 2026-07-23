namespace MSOSync.Plugin.Signing.Abstractions;

/// <summary>
/// Signs a canonical manifest hash using the configured private key.
/// Used by <see cref="MSOSync.Plugin.Packaging.Packager.PluginPackager"/> when a signing key is provided.
/// Implementation: MSOSync.Plugin.Signing.RsaPssPluginSigner (Task 2).
/// </summary>
public interface IPluginSigner
{
    /// <summary>
    /// Sign the given data with the private RSA key.
    /// </summary>
    /// <param name="data">32-byte SHA-256 hash of the canonical manifest JSON (without signature block).</param>
    /// <returns>Base64-standard-encoded RSA-PSS-SHA256 signature bytes.</returns>
    string Sign(ReadOnlySpan<byte> data);

    /// <summary>Identifier of the public key stored in manifest.signature.publicKeyId.</summary>
    string PublicKeyId { get; }
}
