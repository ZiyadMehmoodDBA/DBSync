using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Security;
using MSOSync.Plugin.Signing.Abstractions;
using MSOSync.Plugin.Signing.Models;

namespace MSOSync.Plugin.Signing;

public sealed class RsaPssSignatureVerifier : IPluginSignatureVerifier
{
    private static readonly JsonSerializerOptions CanonicalOpts = new()
    {
        WriteIndented          = false,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ITrustedPublisherRegistry    _registry;
    private readonly PluginSecurityOptions        _opts;
    private readonly ILogger<RsaPssSignatureVerifier> _logger;

    public RsaPssSignatureVerifier(
        ITrustedPublisherRegistry          registry,
        IOptions<PluginSecurityOptions>    options,
        ILogger<RsaPssSignatureVerifier>   logger)
    {
        _registry = registry;
        _opts     = options.Value;
        _logger   = logger;
    }

    public SignatureVerificationResult Verify(ManifestV2 manifest, string manifestJson)
    {
        // 1. No signature block
        if (manifest.Signature is null)
            return SignatureVerificationResult.NoSignature();

        // 2. Algorithm check (case-insensitive)
        if (!string.Equals(manifest.Signature.Algorithm, "RSA-PSS-SHA256",
                StringComparison.OrdinalIgnoreCase))
            return SignatureVerificationResult.UnsupportedAlgorithm(manifest.Signature.Algorithm);

        var keyId = manifest.Signature.PublicKeyId;

        // 3. Trusted publisher lookup
        var keyEntry = _registry.GetPublicKey(keyId);

        // Without the key we cannot verify — treat as UnknownPublisher regardless of RequireTrustedPublisher.
        if (keyEntry is null)
            return SignatureVerificationResult.UnknownPublisher(keyId);

        // 4. Decode Base64 signature
        byte[] sigBytes;
        try
        {
            sigBytes = Convert.FromBase64String(manifest.Signature.Value);
        }
        catch (FormatException)
        {
            return SignatureVerificationResult.InvalidBase64(keyId);
        }

        // 5. Canonical hash: serialize manifest with Signature = null
        var unsigned      = manifest with { Signature = null };
        var canonicalJson = JsonSerializer.Serialize(unsigned, CanonicalOpts);
        var hash          = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));

        _logger.LogDebug(
            new EventId(2003, "PluginSecurity2003"),
            "Hash verification complete for manifest '{ManifestId}', key '{KeyId}'.",
            manifest.Id, keyId);

        // 6. Load public key and verify
        try
        {
            using var rsa = RSA.Create();
            var spki      = Convert.FromBase64String(keyEntry.PublicKeyB64);
            rsa.ImportSubjectPublicKeyInfo(spki, out _);

            var valid = rsa.VerifyHash(
                hash,
                sigBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);

            return valid
                ? SignatureVerificationResult.Valid(keyId)
                : SignatureVerificationResult.InvalidSignature(keyId);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Cryptographic error while verifying signature for key '{KeyId}'.", keyId);
            return SignatureVerificationResult.InvalidSignature(keyId);
        }
    }
}
