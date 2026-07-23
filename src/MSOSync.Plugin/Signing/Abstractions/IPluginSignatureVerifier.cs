using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Signing.Models;

namespace MSOSync.Plugin.Signing.Abstractions;

/// <summary>
/// Verifies the RSA-PSS-SHA256 signature block inside a <see cref="ManifestV2"/> against
/// the canonical manifest hash.
/// </summary>
public interface IPluginSignatureVerifier
{
    /// <summary>
    /// Verify the signature embedded in <paramref name="manifest"/>.
    /// The canonical hash is recomputed from the manifest with Signature = null,
    /// serialized with canonical JSON options, UTF-8 encoded, then SHA-256 hashed.
    /// </summary>
    /// <param name="manifest">Parsed ManifestV2 including the signature block to verify.</param>
    /// <param name="manifestJson">
    ///   Raw UTF-8 JSON as read from the archive (informational; not used for hash computation).
    /// </param>
    SignatureVerificationResult Verify(ManifestV2 manifest, string manifestJson);
}
