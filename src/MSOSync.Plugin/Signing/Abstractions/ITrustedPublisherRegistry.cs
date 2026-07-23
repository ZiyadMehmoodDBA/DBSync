using MSOSync.Plugin.Signing.Models;

namespace MSOSync.Plugin.Signing.Abstractions;

/// <summary>
/// Provides the set of trusted publisher public keys.
/// Loaded once at host startup from <see cref="MSOSync.Plugin.Security.PluginSecurityOptions.TrustedPublishersPath"/>.
/// Expired keys are filtered out at load time.
/// </summary>
public interface ITrustedPublisherRegistry
{
    /// <summary>Retrieve the public key entry for the given key ID. Returns null if not found or expired.</summary>
    PluginSigningKey? GetPublicKey(string publicKeyId);

    /// <summary>All registered non-expired trusted publisher keys.</summary>
    IReadOnlyList<PluginSigningKey> GetAll();
}
