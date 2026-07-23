using System.Security.Cryptography;
using MSOSync.Plugin.Signing.Abstractions;

namespace MSOSync.Plugin.Signing;

/// <summary>
/// Signs canonical manifest hashes using RSA-PSS-SHA256 with a 2048-bit minimum private key.
/// </summary>
public sealed class RsaPssPluginSigner : IPluginSigner, IDisposable
{
    private readonly RSA    _rsa;
    private readonly string _publicKeyId;
    private readonly bool   _ownsKey;

    /// <param name="privateKey">
    ///   RSA private key (2048-bit minimum). The caller owns the key lifetime
    ///   unless <paramref name="ownsKey"/> is true.
    /// </param>
    /// <param name="publicKeyId">Key identifier stored in manifest.signature.publicKeyId.</param>
    /// <param name="ownsKey">When true, Dispose() will dispose the RSA key.</param>
    public RsaPssPluginSigner(RSA privateKey, string publicKeyId, bool ownsKey = false)
    {
        _rsa         = privateKey;
        _publicKeyId = publicKeyId;
        _ownsKey     = ownsKey;
    }

    public string PublicKeyId => _publicKeyId;

    /// <summary>
    /// Sign <paramref name="data"/> (32-byte SHA-256 hash) with RSA-PSS-SHA256.
    /// Returns Base64-standard-encoded signature bytes.
    /// </summary>
    public string Sign(ReadOnlySpan<byte> data)
    {
        // RSA.SignHash operates on a pre-computed hash
        var sigBytes = _rsa.SignHash(
            data.ToArray(),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        return Convert.ToBase64String(sigBytes);
    }

    public void Dispose()
    {
        if (_ownsKey) _rsa.Dispose();
    }
}
