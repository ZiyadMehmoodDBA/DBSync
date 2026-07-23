# Task 2 — Signing: RSA-PSS Models, Interfaces, Implementations, TrustedPublisherRegistry

**Phase:** 2C.1
**Depends on:** nothing (parallel-safe with Task 1)
**Produces:** all Signing models, `IPluginSignatureVerifier`, `ITrustedPublisherRegistry`, `RsaPssPluginSigner`, `RsaPssSignatureVerifier`, `TrustedPublisherRegistry`, `PluginSecurityOptions`, unit tests for signer and verifier

---

## Files to Create

| File | Action |
|------|--------|
| `src/MSOSync.Plugin/Security/PluginSecurityOptions.cs` | Create |
| `src/MSOSync.Plugin/Signing/Models/PluginSigningKey.cs` | Create |
| `src/MSOSync.Plugin/Signing/Models/SignatureVerificationResult.cs` | Create |
| `src/MSOSync.Plugin/Signing/Abstractions/IPluginSignatureVerifier.cs` | Create |
| `src/MSOSync.Plugin/Signing/Abstractions/ITrustedPublisherRegistry.cs` | Create |
| `src/MSOSync.Plugin/Signing/RsaPssPluginSigner.cs` | Create |
| `src/MSOSync.Plugin/Signing/RsaPssSignatureVerifier.cs` | Create |
| `src/MSOSync.Plugin/Signing/TrustedPublisherRegistry.cs` | Create |
| `tests/MSOSync.PluginTests/Signing/RsaPssSignerTests.cs` | Create |
| `tests/MSOSync.PluginTests/Signing/SignatureVerifierTests.cs` | Create |

---

## Interfaces

**Consumes:**
- `ManifestV2`, `ManifestSignatureBlock` (from Task 1 models)
- `IPluginSigner` (interface from Task 1 Step 9)
- `PluginSecurityOptions` (defined in this task)

**Produces for Task 3:**
- `IPluginSignatureVerifier` + `RsaPssSignatureVerifier`
- `ITrustedPublisherRegistry` + `TrustedPublisherRegistry`
- `PluginSigningKey`, `SignatureVerificationResult`
- `RsaPssPluginSigner` (implements `IPluginSigner`)
- `PluginSecurityOptions`

---

## Steps

### Step 1 — Failing tests first: RsaPssSignerTests

- [ ] Create `tests/MSOSync.PluginTests/Signing/RsaPssSignerTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MSOSync.Plugin.Signing;
using Xunit;

namespace MSOSync.PluginTests.Signing;

public sealed class RsaPssSignerTests : IDisposable
{
    private readonly RSA _privateKey;
    private readonly RsaPssPluginSigner _signer;

    public RsaPssSignerTests()
    {
        _privateKey = RSA.Create(2048);
        _signer     = new RsaPssPluginSigner(_privateKey, "test-key-01");
    }

    public void Dispose() => _privateKey.Dispose();

    [Fact]
    public void Sign_ProducesBase64EncodedOutput()
    {
        var data   = SHA256.HashData(Encoding.UTF8.GetBytes("hello world"));
        var result = _signer.Sign(data);

        var decoded = Convert.TryFromBase64String(result, new byte[512], out _);
        decoded.Should().BeTrue("result must be valid Base64");
    }

    [Fact]
    public void Sign_SameInputProducesVerifiableOutput()
    {
        var data      = SHA256.HashData(Encoding.UTF8.GetBytes("canonical manifest json"));
        var signature = _signer.Sign(data);
        var sigBytes  = Convert.FromBase64String(signature);

        // Verify using only the public key
        using var publicKey = RSA.Create();
        publicKey.ImportRSAPublicKey(_privateKey.ExportRSAPublicKey(), out _);

        var valid = publicKey.VerifyHash(
            data.ToArray(),
            sigBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);

        valid.Should().BeTrue("signature produced by the signer must verify with the corresponding public key");
    }

    [Fact]
    public void Sign_PublicKeyId_MatchesConstructorValue()
        => _signer.PublicKeyId.Should().Be("test-key-01");

    [Fact]
    public void Sign_EmptyData_ProducesVerifiableOutput()
    {
        // Edge case: signing an empty hash (all zeros)
        var data      = new byte[32];
        var signature = _signer.Sign(data);
        signature.Should().NotBeNullOrEmpty();
        Convert.TryFromBase64String(signature, new byte[512], out _).Should().BeTrue();
    }
}
```

### Step 2 — Failing tests: SignatureVerifierTests

- [ ] Create `tests/MSOSync.PluginTests/Signing/SignatureVerifierTests.cs`:

```csharp
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
    private readonly RSA _rsa;
    private readonly PluginSigningKey _signingKey;
    private readonly TrustedPublisherRegistry _registry;
    private readonly RsaPssSignatureVerifier _verifier;

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
        var unsigned     = manifest with { Signature = null };
        var canonicalJson = JsonSerializer.Serialize(unsigned, CanonicalOpts);
        var hash         = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        var sigBytes     = _rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var sigValue     = Convert.ToBase64String(sigBytes);
        var signed       = manifest with
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
```

### Step 3 — Implement PluginSecurityOptions

- [ ] Create `src/MSOSync.Plugin/Security/PluginSecurityOptions.cs`:

```csharp
namespace MSOSync.Plugin.Security;

public sealed class PluginSecurityOptions
{
    /// <summary>
    /// When true, all .msopkg packages must carry a valid signature from a trusted publisher.
    /// When false, unsigned packages are accepted (local dev mode).
    /// A present-but-invalid signature always fails regardless of this setting.
    /// Default: false.
    /// </summary>
    public bool RequireSignedPackages { get; set; } = false;

    /// <summary>
    /// When true, signed packages must additionally have their publisher in the trusted registry.
    /// Has no effect when RequireSignedPackages = false and the package has no signature block.
    /// Default: true.
    /// </summary>
    public bool RequireTrustedPublisher { get; set; } = true;

    /// <summary>
    /// Path to the trusted publishers JSON file.
    /// Resolved relative to AppContext.BaseDirectory.
    /// Default: "trusted-publishers.json".
    /// </summary>
    public string TrustedPublishersPath { get; set; } = "trusted-publishers.json";

    /// <summary>
    /// Algorithm for IPluginSigner. Supported values: "RSA-PSS-SHA256".
    /// Default: "RSA-PSS-SHA256".
    /// </summary>
    public string PreferredSigningAlgorithm { get; set; } = "RSA-PSS-SHA256";
}
```

### Step 4 — Implement Signing models

- [ ] Create `src/MSOSync.Plugin/Signing/Models/PluginSigningKey.cs`:

```csharp
namespace MSOSync.Plugin.Signing.Models;

public sealed record PluginSigningKey
{
    /// <summary>Unique key identifier. Matches manifest.signature.publicKeyId.</summary>
    public string  KeyId        { get; init; } = null!;

    /// <summary>Human-readable publisher name.</summary>
    public string  Publisher    { get; init; } = null!;

    /// <summary>
    /// Base64-standard-encoded DER SubjectPublicKeyInfo of the RSA-2048 public key.
    /// Loaded via RSA.Create().ImportSubjectPublicKeyInfo(...).
    /// </summary>
    public string  PublicKeyB64 { get; init; } = null!;

    /// <summary>ISO-8601 UTC datetime when this key was added to the registry.</summary>
    public string  AddedAt      { get; init; } = null!;

    /// <summary>Optional ISO-8601 UTC expiry. Null = never expires.</summary>
    public string? ExpiresAt    { get; init; }
}
```

- [ ] Create `src/MSOSync.Plugin/Signing/Models/SignatureVerificationResult.cs`:

```csharp
namespace MSOSync.Plugin.Signing.Models;

public enum SignatureVerificationOutcome
{
    Valid,
    NoSignature,
    UnknownPublisher,
    InvalidBase64,
    InvalidSignature,
    UnsupportedAlgorithm,
}

public sealed record SignatureVerificationResult(
    SignatureVerificationOutcome Outcome,
    string?                      PublicKeyId,
    string?                      ErrorMessage)
{
    public bool IsValid => Outcome == SignatureVerificationOutcome.Valid;

    public static SignatureVerificationResult Valid(string publicKeyId)
        => new(SignatureVerificationOutcome.Valid, publicKeyId, null);

    public static SignatureVerificationResult NoSignature()
        => new(SignatureVerificationOutcome.NoSignature, null, "Manifest contains no signature block.");

    public static SignatureVerificationResult UnknownPublisher(string keyId)
        => new(SignatureVerificationOutcome.UnknownPublisher, keyId,
               $"Public key ID '{keyId}' is not in the trusted publisher registry.");

    public static SignatureVerificationResult InvalidBase64(string keyId)
        => new(SignatureVerificationOutcome.InvalidBase64, keyId,
               "Signature value is not valid Base64.");

    public static SignatureVerificationResult InvalidSignature(string keyId)
        => new(SignatureVerificationOutcome.InvalidSignature, keyId,
               "Signature does not match the canonical manifest hash.");

    public static SignatureVerificationResult UnsupportedAlgorithm(string algorithm)
        => new(SignatureVerificationOutcome.UnsupportedAlgorithm, null,
               $"Signing algorithm '{algorithm}' is not supported. Expected 'RSA-PSS-SHA256'.");
}
```

### Step 5 — Implement signing interfaces

- [ ] Create `src/MSOSync.Plugin/Signing/Abstractions/IPluginSignatureVerifier.cs`:

```csharp
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
```

- [ ] Create `src/MSOSync.Plugin/Signing/Abstractions/ITrustedPublisherRegistry.cs`:

```csharp
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
```

### Step 6 — Implement RsaPssPluginSigner

- [ ] Create `src/MSOSync.Plugin/Signing/RsaPssPluginSigner.cs`:

```csharp
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

    private readonly bool _ownsKey;

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
```

### Step 7 — Implement TrustedPublisherRegistry

The registry accepts an optional seed list (for testing) and otherwise reads from a JSON file.

- [ ] Create `src/MSOSync.Plugin/Signing/TrustedPublisherRegistry.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Security;
using MSOSync.Plugin.Signing.Abstractions;
using MSOSync.Plugin.Signing.Models;

namespace MSOSync.Plugin.Signing;

/// <summary>
/// Loads trusted publisher public keys from the JSON file at startup.
/// Expired keys are filtered out. Cached in memory for O(1) lookup.
/// </summary>
public sealed class TrustedPublisherRegistry : ITrustedPublisherRegistry
{
    private readonly Dictionary<string, PluginSigningKey> _keys;
    private readonly ILogger<TrustedPublisherRegistry>    _logger;

    private static readonly EventId ExpiredKeySkipped = new(2001, "PluginSecurity2001");

    /// <summary>Primary constructor: loads from file specified in options.</summary>
    public TrustedPublisherRegistry(
        IOptions<PluginSecurityOptions>      options,
        ILogger<TrustedPublisherRegistry>    logger)
        : this(options, logger, LoadFromFile(options.Value, logger)) { }

    /// <summary>Internal/test constructor: accepts a pre-built list of keys.</summary>
    internal TrustedPublisherRegistry(
        IOptions<PluginSecurityOptions>      options,
        ILogger<TrustedPublisherRegistry>    logger,
        IEnumerable<PluginSigningKey>        keys)
    {
        _logger = logger;
        _keys   = new Dictionary<string, PluginSigningKey>(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var key in keys)
        {
            if (key.ExpiresAt is not null &&
                DateTime.TryParse(key.ExpiresAt, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var expiry) &&
                expiry < now)
            {
                _logger.Log(LogLevel.Warning, ExpiredKeySkipped,
                    "Trusted publisher key '{KeyId}' (publisher: '{Publisher}') expired at {Expiry} — skipping.",
                    key.KeyId, key.Publisher, key.ExpiresAt);
                continue;
            }
            _keys[key.KeyId] = key;
        }
    }

    public PluginSigningKey? GetPublicKey(string publicKeyId)
        => _keys.TryGetValue(publicKeyId, out var key) ? key : null;

    public IReadOnlyList<PluginSigningKey> GetAll()
        => _keys.Values.ToList().AsReadOnly();

    // ── file loader ───────────────────────────────────────────────────────

    private static IEnumerable<PluginSigningKey> LoadFromFile(
        PluginSecurityOptions options, ILogger logger)
    {
        var path = Path.IsPathRooted(options.TrustedPublishersPath)
            ? options.TrustedPublishersPath
            : Path.Combine(AppContext.BaseDirectory, options.TrustedPublishersPath);

        if (!File.Exists(path))
        {
            logger.LogWarning(
                "Trusted publishers file '{Path}' not found. Plugin signature verification will use an empty registry.",
                path);
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc  = JsonSerializer.Deserialize<TrustedPublishersFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return doc?.Publishers ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load trusted publishers from '{Path}'.", path);
            return [];
        }
    }

    private sealed class TrustedPublishersFile
    {
        public List<PluginSigningKey> Publishers { get; init; } = [];
    }
}
```

### Step 8 — Implement RsaPssSignatureVerifier

- [ ] Create `src/MSOSync.Plugin/Signing/RsaPssSignatureVerifier.cs`:

```csharp
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

    private readonly ITrustedPublisherRegistry   _registry;
    private readonly PluginSecurityOptions       _opts;
    private readonly ILogger<RsaPssSignatureVerifier> _logger;

    public RsaPssSignatureVerifier(
        ITrustedPublisherRegistry         registry,
        IOptions<PluginSecurityOptions>   options,
        ILogger<RsaPssSignatureVerifier>  logger)
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
        if (keyEntry is null && _opts.RequireTrustedPublisher)
            return SignatureVerificationResult.UnknownPublisher(keyId);

        // If RequireTrustedPublisher = false but key not found, we still need a public key to verify.
        // Without the key we cannot verify — treat as UnknownPublisher.
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
        var unsigned     = manifest with { Signature = null };
        var canonicalJson = JsonSerializer.Serialize(unsigned, CanonicalOpts);
        var hash         = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));

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
```

### Step 9 — Build and run unit tests

- [ ] Run: `dotnet build src/MSOSync.Plugin/MSOSync.Plugin.csproj -c Debug 2>&1 | Select-Object -Last 30`

  Expected: 0 errors.

- [ ] Run: `dotnet test tests/MSOSync.PluginTests/MSOSync.PluginTests.csproj --filter "FullyQualifiedName~RsaPssSignerTests|FullyQualifiedName~SignatureVerifierTests" -c Debug 2>&1 | Select-Object -Last 40`

  Expected: all tests pass.

### Step 10 — Commit

- [ ] Stage files:

```
git add src/MSOSync.Plugin/Security/PluginSecurityOptions.cs
git add src/MSOSync.Plugin/Signing/Models/PluginSigningKey.cs
git add src/MSOSync.Plugin/Signing/Models/SignatureVerificationResult.cs
git add src/MSOSync.Plugin/Signing/Abstractions/IPluginSignatureVerifier.cs
git add src/MSOSync.Plugin/Signing/Abstractions/ITrustedPublisherRegistry.cs
git add src/MSOSync.Plugin/Signing/RsaPssPluginSigner.cs
git add src/MSOSync.Plugin/Signing/RsaPssSignatureVerifier.cs
git add src/MSOSync.Plugin/Signing/TrustedPublisherRegistry.cs
git add tests/MSOSync.PluginTests/Signing/RsaPssSignerTests.cs
git add tests/MSOSync.PluginTests/Signing/SignatureVerifierTests.cs
```

- [ ] Commit:

```
git commit -m "feat(2C.1-T2): signing models, RSA-PSS signer/verifier, TrustedPublisherRegistry, PluginSecurityOptions"
```
