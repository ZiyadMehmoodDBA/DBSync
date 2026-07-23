using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Security;
using MSOSync.Plugin.Signing;
using MSOSync.Plugin.Signing.Models;
using Xunit;

namespace MSOSync.PluginTests.Signing;

public sealed class SignatureVerifierTests : IDisposable
{
    private readonly RSA                   _rsa;
    private readonly PluginSigningKey      _signingKey;
    private readonly TrustedPublisherRegistry  _registry;
    private readonly RsaPssSignatureVerifier   _verifier;

    private static readonly JsonSerializerOptions CanonicalOpts = new()
    {
        WriteIndented          = false,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public SignatureVerifierTests()
    {
        _rsa = RSA.Create(2048);
        _signingKey = new PluginSigningKey
        {
            KeyId        = "test-key-01",
            Publisher    = "Test Publisher",
            PublicKeyB64 = Convert.ToBase64String(_rsa.ExportSubjectPublicKeyInfo()),
            AddedAt      = "2024-01-01T00:00:00Z",
            ExpiresAt    = null,
        };
        _registry = new TrustedPublisherRegistry(
            Options.Create(new PluginSecurityOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TrustedPublisherRegistry>.Instance,
            [_signingKey]);
        _verifier = new RsaPssSignatureVerifier(
            _registry,
            Options.Create(new PluginSecurityOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RsaPssSignatureVerifier>.Instance);
    }

    public void Dispose() => _rsa.Dispose();

    private ManifestV2 BuildManifest(ManifestSignatureBlock? sig = null) => new()
    {
        ManifestVersion      = 2,
        Id                   = "test.plugin",
        Name                 = "Test",
        Version              = "1.0.0",
        SdkVersion           = "1.0",
        SdkVersionConstraint = ">=1.0.0 <2.0.0",
        ApiVersion           = "1",
        MinHostVersion       = "1.0.0",
        MaxHostVersion       = "99.0.0",
        EntryAssembly        = "Test.dll",
        EntryType            = "Test.Plugin",
        Author               = "Author",
        Description          = "Desc.",
        Files                = [new PackageFileEntry { Path = "Test.dll", Sha256 = new string('a', 64) }],
        Signature            = sig,
    };

    private (ManifestV2 signed, string rawJson) SignManifest(ManifestV2 manifest)
    {
        var unsigned      = manifest with { Signature = null };
        var canonicalJson = JsonSerializer.Serialize(unsigned, CanonicalOpts);
        var hash          = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        var sigBytes      = _rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var sigValue      = Convert.ToBase64String(sigBytes);
        var signed        = manifest with
        {
            Signature = new ManifestSignatureBlock
            {
                Algorithm   = "RSA-PSS-SHA256",
                PublicKeyId = "test-key-01",
                Value       = sigValue,
            },
        };
        var rawJson = JsonSerializer.Serialize(signed, CanonicalOpts);
        return (signed, rawJson);
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsValid()
    {
        var (signed, rawJson) = SignManifest(BuildManifest());
        var result = _verifier.Verify(signed, rawJson);
        result.Outcome.Should().Be(SignatureVerificationOutcome.Valid);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_NoSignatureBlock_ReturnsNoSignature()
    {
        var manifest = BuildManifest(null);
        var rawJson  = JsonSerializer.Serialize(manifest, CanonicalOpts);
        var result   = _verifier.Verify(manifest, rawJson);
        result.Outcome.Should().Be(SignatureVerificationOutcome.NoSignature);
    }

    [Fact]
    public void Verify_UnknownPublisher_ReturnsUnknownPublisher()
    {
        var (signed, _) = SignManifest(BuildManifest());
        var tampered    = signed with
        {
            Signature = signed.Signature! with { PublicKeyId = "unknown-key-99" },
        };
        var rawJson = JsonSerializer.Serialize(tampered, CanonicalOpts);
        var result  = _verifier.Verify(tampered, rawJson);
        result.Outcome.Should().Be(SignatureVerificationOutcome.UnknownPublisher);
    }

    [Fact]
    public void Verify_InvalidBase64_ReturnsInvalidBase64()
    {
        var manifest = BuildManifest(new ManifestSignatureBlock
        {
            Algorithm   = "RSA-PSS-SHA256",
            PublicKeyId = "test-key-01",
            Value       = "not!!!valid-base64",
        });
        var rawJson = JsonSerializer.Serialize(manifest, CanonicalOpts);
        var result  = _verifier.Verify(manifest, rawJson);
        result.Outcome.Should().Be(SignatureVerificationOutcome.InvalidBase64);
    }

    [Fact]
    public void Verify_TamperedManifest_ReturnsInvalidSignature()
    {
        var (signed, _) = SignManifest(BuildManifest());
        // Modify a field AFTER signing
        var tampered = signed with { Name = "TAMPERED" };
        var rawJson  = JsonSerializer.Serialize(tampered, CanonicalOpts);
        var result   = _verifier.Verify(tampered, rawJson);
        result.Outcome.Should().Be(SignatureVerificationOutcome.InvalidSignature);
    }

    [Fact]
    public void Verify_UnsupportedAlgorithm_ReturnsUnsupportedAlgorithm()
    {
        var manifest = BuildManifest(new ManifestSignatureBlock
        {
            Algorithm   = "Ed448",
            PublicKeyId = "test-key-01",
            Value       = "AAAA==",
        });
        var rawJson = JsonSerializer.Serialize(manifest, CanonicalOpts);
        var result  = _verifier.Verify(manifest, rawJson);
        result.Outcome.Should().Be(SignatureVerificationOutcome.UnsupportedAlgorithm);
    }

    [Fact]
    public void Verify_ExpiredKey_TreatedAsUnknownPublisher()
    {
        // Build a registry that has the key but with a past expiry
        var expiredKey = _signingKey with { ExpiresAt = "2020-01-01T00:00:00Z" };
        var registry   = new TrustedPublisherRegistry(
            Options.Create(new PluginSecurityOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TrustedPublisherRegistry>.Instance,
            [expiredKey]);
        var verifier = new RsaPssSignatureVerifier(
            registry,
            Options.Create(new PluginSecurityOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RsaPssSignatureVerifier>.Instance);

        var (signed, rawJson) = SignManifest(BuildManifest());
        var result = verifier.Verify(signed, rawJson);
        // Expired key is filtered at registry load, so it's treated as unknown publisher
        result.Outcome.Should().Be(SignatureVerificationOutcome.UnknownPublisher);
    }
}
