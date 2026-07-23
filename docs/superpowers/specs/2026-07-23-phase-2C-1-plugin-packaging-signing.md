# Phase 2C.1: Plugin Packaging & Signing — Design Specification

**Date:** 2026-07-23
**Status:** Approved
**Phase:** 2C — SDK & Ecosystem
**Scope:** `.msopkg` package format, manifest v2 schema, Ed25519 signing and verification, tamper detection, `IPluginPackager`, `IPluginInstaller`. No breaking changes to `IPlugin`, `IPluginContext`, or `PluginManifest`.

---

## Goal

Define and implement a standardized, signed `.msopkg` package format for MSOSync plugins such that plugin artifacts can be built, distributed, cryptographically verified, and installed by the host without manual file copying.

---

## Architecture

### Component Map

```
src\
├── MSOSync.Plugin\
│   ├── Packaging\
│   │   ├── Abstractions\
│   │   │   ├── IPluginPackager.cs          ← creates .msopkg from project dir
│   │   │   └── IPluginInstaller.cs         ← installs .msopkg into plugins dir
│   │   ├── Models\
│   │   │   ├── ManifestV2.cs               ← manifest v2 schema (additive, backward-compat)
│   │   │   ├── ManifestSignatureBlock.cs   ← nested signature JSON block
│   │   │   ├── PackageFileEntry.cs         ← manifest hash entry for one file
│   │   │   ├── PackagingOptions.cs         ← IOptions<PackagingOptions>
│   │   │   └── PackageInstallResult.cs     ← outcome of IPluginInstaller.InstallAsync
│   │   ├── Packager\
│   │   │   └── PluginPackager.cs           ← implements IPluginPackager
│   │   └── Installer\
│   │       └── PluginInstaller.cs          ← implements IPluginInstaller
│   │
│   ├── Signing\
│   │   ├── Abstractions\
│   │   │   ├── IPluginSignatureVerifier.cs
│   │   │   ├── IPluginSigner.cs
│   │   │   └── ITrustedPublisherRegistry.cs
│   │   ├── Models\
│   │   │   ├── PluginSigningKey.cs         ← public key entry in trusted registry
│   │   │   └── SignatureVerificationResult.cs
│   │   ├── Ed25519PluginSigner.cs
│   │   ├── Ed25519SignatureVerifier.cs
│   │   └── TrustedPublisherRegistry.cs
│   │
│   └── Security\
│       └── PluginSecurityOptions.cs        ← IOptions<PluginSecurityOptions>
│
├── MSOSync.Persistence\
│   └── Entities\SyncPlugin.cs              ← extend with 4 new nullable columns (no new migration)
│
tests\
└── MSOSync.PluginTests\
    ├── Packaging\
    │   ├── PluginPackagerTests.cs
    │   └── PluginInstallerTests.cs
    └── Signing\
        ├── Ed25519SignerTests.cs
        └── Ed25519VerifierTests.cs
```

### Dependency Rules

- `MSOSync.Plugin.Packaging` and `MSOSync.Plugin.Signing` depend only on `MSOSync.Common` and `MSOSync.Sdk`.
- Neither namespace references `MSOSync.Persistence` directly. The store abstraction (`IPluginStore`) already lives in `MSOSync.Plugin.Abstractions`; `IPluginInstaller` calls it through the existing interface.
- `System.IO.Compression` (built-in) handles ZIP operations. `System.Security.Cryptography` (built-in .NET 9) provides Ed25519 and SHA-256.
- No new NuGet packages are required.

### Data Flow — Package Creation

```
Plugin project dir
       │
       ▼
IPluginPackager.PackageAsync(sourceDir, outputPath, signingKey?)
       │
       ├─ 1. Read and validate plugin.json as ManifestV2
       ├─ 2. Compute SHA-256 hash of every listed DLL + plugin.config.json
       ├─ 3. Inject files[] hash table into manifest
       ├─ 4. Optionally sign: Ed25519(SHA-256(canonical manifest JSON)) → base64
       ├─ 5. Inject signature block into manifest
       └─ 6. Write ZipArchive (.msopkg):
              manifest.json
              plugin.dll (+ dep DLLs from lib/)
              plugin.config.json
              assets/  (optional)
```

### Data Flow — Installation

```
.msopkg file
       │
       ▼
IPluginInstaller.InstallAsync(packagePath, ct)
       │
       ├─ 1. Open ZipArchive, extract manifest.json
       ├─ 2. Parse as ManifestV2, run schema validation
       ├─ 3. Check sdkVersionConstraint against host SDK version
       ├─ 4. Signature verification (IPluginSignatureVerifier)
       │      ├─ Required if manifest.signature present OR PluginSecurityOptions.RequireSignedPackages = true
       │      └─ Skipped for local dev plugins when RequireSignedPackages = false
       ├─ 5. DLL hash verification: recompute SHA-256 of each entry in manifest.files[],
       │      compare against manifest value, fail on first mismatch
       ├─ 6. Unpack all entries to temp directory
       ├─ 7. Atomic move: rename temp dir → plugins/{pluginId}
       │      (replaces existing directory for upgrades)
       ├─ 8. IPluginStore.UpsertAsync with SignedBy, SignatureAlgorithm, PackageHash
       └─ 9. Return PackageInstallResult
```

---

## Package Format

### File Extension and MIME Type

| Property | Value |
|---|---|
| Extension | `.msopkg` |
| Container | ZIP (PKZIP 2.0, deflate compression) |
| MIME type | `application/vnd.msosync.plugin` |

### Directory Structure Inside the Archive

```
<archive-root>/
├── manifest.json              ← required; ManifestV2 schema (see below)
├── {EntryAssembly}.dll        ← required; matches manifest.entryAssembly
├── lib/                       ← optional; private dependency DLLs
│   └── *.dll
├── plugin.config.json         ← optional; default plugin configuration
└── assets/                    ← optional; icons, documentation
    ├── icon.png               ← if present, must be ≤ 128 KB, PNG or SVG
    └── README.md
```

Rules:
- The archive must contain exactly one `manifest.json` at the root level.
- All paths in `manifest.files[]` are relative to the archive root and must not contain `..` or absolute path separators.
- The `assets/` directory may contain arbitrary files. Asset files are not hash-verified (they are not executable). A maximum of 20 asset files and 2 MB total for the assets directory is enforced by `IPluginInstaller`.
- `lib/` DLLs that are listed in `manifest.files[]` are hash-verified; unlisted files in `lib/` are not installed.

---

## Manifest v2 Schema — `manifest.json`

### Full JSON Example

```json
{
  "manifestVersion": 2,
  "id": "msosync.sqlserver.collector",
  "name": "SQL Server Collector",
  "version": "2.1.0",
  "sdkVersion": "1.0",
  "sdkVersionConstraint": ">=1.0.0 <2.0.0",
  "apiVersion": "1",
  "startupOrder": 500,
  "minHostVersion": "15.0.0",
  "maxHostVersion": "15.9.999",
  "entryAssembly": "MSOSync.SqlCollector.dll",
  "entryType": "MSOSync.SqlCollector.Plugin",
  "author": "Acme Corp",
  "authorEmail": "plugins@acme.example",
  "homepage": "https://acme.example/msosync-plugins/sqlserver",
  "license": "MIT",
  "description": "Collects change events from SQL Server via CDC.",
  "keywords": ["sql", "cdc", "sqlserver"],
  "capabilities": ["Collector"],
  "permissions": ["Collectors"],
  "pluginDependencies": [
    {
      "id": "msosync.sqlserver.common",
      "versionRange": ">=1.0.0 <3.0.0"
    }
  ],
  "files": [
    {
      "path": "MSOSync.SqlCollector.dll",
      "sha256": "a3f5c2d1e8b74a9f0c6d2e5b8f1a4c7e9d0f3a6b2c8e1d4f7a0b3c6e9d2f5a8b"
    },
    {
      "path": "lib/MSOSync.SqlServer.Common.dll",
      "sha256": "b2e4d6f8a0c2e4f6a8b0d2e4f6a8b0c2d4e6f8a0b2c4d6e8f0a2b4c6d8e0f2a4"
    },
    {
      "path": "plugin.config.json",
      "sha256": "c4d8f0a4b8c0d4f8a0b4c8d0f4a8b0c4d8f0a4b8c0d4f8a0b4c8d0f4a8b0c4d8"
    }
  ],
  "signature": {
    "algorithm": "Ed25519",
    "publicKeyId": "acme-corp-2024-01",
    "value": "MEQCIBz...base64url-encoded-64-bytes...=="
  }
}
```

### Field Definitions

| Field | Type | Required | v2 New | Description |
|---|---|---|---|---|
| `manifestVersion` | int | yes | — | Must be `2` for packages. Existing host still accepts `1` for directory-based plugins. |
| `id` | string | yes | — | Unique plugin identifier (reverse-DNS style, no path separators, no `..`) |
| `name` | string | yes | — | Human-readable display name |
| `version` | string | yes | — | Plugin version, parseable as `System.Version` (major.minor.patch[.build]) |
| `sdkVersion` | string | yes | — | Exact SDK version the plugin was compiled against (e.g. `"1.0"`) |
| `sdkVersionConstraint` | string | yes | yes | Semver range the plugin accepts (e.g. `">=1.0.0 <2.0.0"`). Parsed by `SdkVersionConstraintParser`. |
| `apiVersion` | string | yes | — | Integer string (e.g. `"1"`). Must match `PluginHostOptions.SupportedApiVersion`. |
| `startupOrder` | int | no | — | Lower values start first. Default `1000`. |
| `minHostVersion` | string | yes | — | Minimum MSOSync host version inclusive. |
| `maxHostVersion` | string | yes | — | Maximum MSOSync host version inclusive. |
| `entryAssembly` | string | yes | — | DLL filename at archive root. No path separators. |
| `entryType` | string | yes | — | Fully-qualified type name of the `IPlugin` implementation. |
| `author` | string | yes | — | Human-readable author name. |
| `authorEmail` | string | no | yes | Contact email. Informational only; not verified. |
| `homepage` | string | no | yes | URL of plugin documentation or repository. |
| `license` | string | no | yes | SPDX license identifier (e.g. `"MIT"`, `"Apache-2.0"`). |
| `description` | string | yes | — | One-sentence plugin description. |
| `keywords` | string[] | no | yes | Search keywords for marketplace indexing. Maximum 10 entries. |
| `capabilities` | string[] | no | — | Declared `PluginCapability` names. |
| `permissions` | string[] | no | — | Declared `PluginPermission` names. |
| `pluginDependencies` | object[] | no | yes | Structured dependency list (replaces flat `dependencies` string array). Each entry has `id` (string) and `versionRange` (string). |
| `files` | object[] | yes | yes | Hash manifest for all executable files (DLLs) and `plugin.config.json`. Each entry has `path` (string, relative to archive root) and `sha256` (string, lowercase hex, 64 chars). |
| `signature` | object | no | yes | Cryptographic signature block. Absent for unsigned local-dev plugins. |

### `signature` Block Fields

| Field | Type | Required in block | Description |
|---|---|---|---|
| `algorithm` | string | yes | Must be `"Ed25519"`. |
| `publicKeyId` | string | yes | Identifier of the signer's public key in the trusted publisher registry. |
| `value` | string | yes | Base64-standard-encoded 64-byte Ed25519 signature over the canonical manifest hash. |

### `pluginDependencies` Entry Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Plugin ID of the required dependency. |
| `versionRange` | string | yes | Semver range string. Parsed by `SdkVersionConstraintParser`. Example: `">=1.0.0 <3.0.0"`. |

### Backward Compatibility

`PluginManifest` (v1, `manifestVersion: 1`) is not changed. It remains the model used by the existing `PluginLoader` for directory-based plugin discovery. `ManifestV2` is a separate record in `MSOSync.Plugin.Packaging.Models`. The installer converts a `ManifestV2` into a `PluginManifest` before calling existing loading infrastructure.

The `dependencies` string array field in `PluginManifest` is preserved unchanged. When `IPluginInstaller` writes to the plugins directory it also writes a `plugin.json` (v1 manifest) derived from the `ManifestV2` so the existing `PluginLoader` can pick it up on next host restart.

---

## C# Models

### `ManifestV2`

```csharp
// src/MSOSync.Plugin/Packaging/Models/ManifestV2.cs
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record ManifestV2
{
    [JsonPropertyName("manifestVersion")]      public int     ManifestVersion      { get; init; } = 2;
    [JsonPropertyName("id")]                   public string  Id                   { get; init; } = null!;
    [JsonPropertyName("name")]                 public string  Name                 { get; init; } = null!;
    [JsonPropertyName("version")]              public string  Version              { get; init; } = null!;
    [JsonPropertyName("sdkVersion")]           public string  SdkVersion           { get; init; } = null!;
    [JsonPropertyName("sdkVersionConstraint")] public string  SdkVersionConstraint { get; init; } = null!;
    [JsonPropertyName("apiVersion")]           public string  ApiVersion           { get; init; } = null!;
    [JsonPropertyName("startupOrder")]         public int     StartupOrder         { get; init; } = 1000;
    [JsonPropertyName("minHostVersion")]       public string  MinHostVersion       { get; init; } = null!;
    [JsonPropertyName("maxHostVersion")]       public string  MaxHostVersion       { get; init; } = null!;
    [JsonPropertyName("entryAssembly")]        public string  EntryAssembly        { get; init; } = null!;
    [JsonPropertyName("entryType")]            public string  EntryType            { get; init; } = null!;
    [JsonPropertyName("author")]               public string  Author               { get; init; } = null!;
    [JsonPropertyName("authorEmail")]          public string? AuthorEmail          { get; init; }
    [JsonPropertyName("homepage")]             public string? Homepage             { get; init; }
    [JsonPropertyName("license")]              public string? License              { get; init; }
    [JsonPropertyName("description")]          public string  Description          { get; init; } = null!;
    [JsonPropertyName("keywords")]             public IReadOnlyList<string> Keywords            { get; init; } = [];
    [JsonPropertyName("capabilities")]         public IReadOnlyList<string> Capabilities        { get; init; } = [];
    [JsonPropertyName("permissions")]          public IReadOnlyList<string> Permissions         { get; init; } = [];
    [JsonPropertyName("pluginDependencies")]   public IReadOnlyList<PluginDependencyEntry> PluginDependencies { get; init; } = [];
    [JsonPropertyName("files")]                public IReadOnlyList<PackageFileEntry> Files      { get; init; } = [];
    [JsonPropertyName("signature")]            public ManifestSignatureBlock? Signature         { get; init; }
}
```

### `PackageFileEntry`

```csharp
// src/MSOSync.Plugin/Packaging/Models/PackageFileEntry.cs
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record PackageFileEntry
{
    [JsonPropertyName("path")]   public string Path   { get; init; } = null!;
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = null!;
}
```

### `PluginDependencyEntry`

```csharp
// src/MSOSync.Plugin/Packaging/Models/PluginDependencyEntry.cs
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record PluginDependencyEntry
{
    [JsonPropertyName("id")]           public string Id           { get; init; } = null!;
    [JsonPropertyName("versionRange")] public string VersionRange { get; init; } = null!;
}
```

### `ManifestSignatureBlock`

```csharp
// src/MSOSync.Plugin/Packaging/Models/ManifestSignatureBlock.cs
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record ManifestSignatureBlock
{
    [JsonPropertyName("algorithm")]   public string Algorithm   { get; init; } = null!;
    [JsonPropertyName("publicKeyId")] public string PublicKeyId { get; init; } = null!;
    [JsonPropertyName("value")]       public string Value       { get; init; } = null!;
}
```

### `PackageInstallResult`

```csharp
// src/MSOSync.Plugin/Packaging/Models/PackageInstallResult.cs
namespace MSOSync.Plugin.Packaging.Models;

public sealed record PackageInstallResult(
    bool    Success,
    string  PluginId,
    string? InstalledVersion,
    string? FailureStage,
    string? ErrorMessage)
{
    public static PackageInstallResult Ok(string pluginId, string version)
        => new(true, pluginId, version, null, null);

    public static PackageInstallResult Fail(string pluginId, string stage, string error)
        => new(false, pluginId, null, stage, error);
}
```

### `PackagingOptions`

```csharp
// src/MSOSync.Plugin/Packaging/Models/PackagingOptions.cs
namespace MSOSync.Plugin.Packaging.Models;

public sealed class PackagingOptions
{
    /// <summary>Maximum size of a .msopkg archive in bytes. Default: 50 MB.</summary>
    public long MaxPackageSizeBytes { get; set; } = 52_428_800;

    /// <summary>Maximum number of files inside the archive. Default: 200.</summary>
    public int MaxFileCount { get; set; } = 200;

    /// <summary>Maximum total size of the assets/ directory in bytes. Default: 2 MB.</summary>
    public long MaxAssetsSizeBytes { get; set; } = 2_097_152;

    /// <summary>Maximum number of files inside assets/. Default: 20.</summary>
    public int MaxAssetsFileCount { get; set; } = 20;
}
```

### `PluginSecurityOptions`

```csharp
// src/MSOSync.Plugin/Security/PluginSecurityOptions.cs
namespace MSOSync.Plugin.Security;

public sealed class PluginSecurityOptions
{
    /// <summary>
    /// When true, all .msopkg packages must carry a valid signature from a trusted publisher.
    /// When false, unsigned packages are accepted (local dev mode).
    /// Default: false.
    /// </summary>
    public bool RequireSignedPackages { get; set; } = false;

    /// <summary>
    /// When true, signed packages must additionally have their publisher in the trusted registry.
    /// Has no effect when RequireSignedPackages = false and the package has no signature block.
    /// Default: true.
    /// </summary>
    public bool RequireTrustedPublisher { get; set; } = true;

    /// <summary>Path to the trusted publishers JSON file. Resolved relative to AppContext.BaseDirectory.</summary>
    public string TrustedPublishersPath { get; set; } = "trusted-publishers.json";
}
```

---

## Signing Interfaces

### `IPluginSigner`

```csharp
// src/MSOSync.Plugin/Signing/Abstractions/IPluginSigner.cs
namespace MSOSync.Plugin.Signing.Abstractions;

/// <summary>
/// Signs a canonical manifest hash using the configured private key.
/// Used by IPluginPackager when a signing key is provided.
/// </summary>
public interface IPluginSigner
{
    /// <summary>
    /// Sign the given data with the private Ed25519 key.
    /// </summary>
    /// <param name="data">Canonical UTF-8 bytes to sign (SHA-256 hash of manifest JSON without signature block).</param>
    /// <returns>64-byte Ed25519 signature, Base64-standard encoded.</returns>
    string Sign(ReadOnlySpan<byte> data);

    /// <summary>Identifier of the public key, stored in manifest.signature.publicKeyId.</summary>
    string PublicKeyId { get; }
}
```

### `IPluginSignatureVerifier`

```csharp
// src/MSOSync.Plugin/Signing/Abstractions/IPluginSignatureVerifier.cs
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Signing.Models;

namespace MSOSync.Plugin.Signing.Abstractions;

/// <summary>
/// Verifies the signature block inside a ManifestV2 against the canonical manifest hash.
/// </summary>
public interface IPluginSignatureVerifier
{
    /// <summary>
    /// Verify the signature embedded in <paramref name="manifest"/>.
    /// The canonical hash is recomputed internally from the manifest JSON excluding the
    /// signature block (serialize manifest with Signature = null, UTF-8 encode, SHA-256).
    /// </summary>
    /// <param name="manifest">Parsed ManifestV2 including the signature block to verify.</param>
    /// <param name="manifestJson">
    ///   The raw UTF-8 JSON string as read from the archive (before any parsing).
    ///   Used to guard against round-trip serialization differences.
    /// </param>
    /// <returns>A <see cref="SignatureVerificationResult"/> describing success or the specific failure.</returns>
    SignatureVerificationResult Verify(ManifestV2 manifest, string manifestJson);
}
```

### `ITrustedPublisherRegistry`

```csharp
// src/MSOSync.Plugin/Signing/Abstractions/ITrustedPublisherRegistry.cs
using MSOSync.Plugin.Signing.Models;

namespace MSOSync.Plugin.Signing.Abstractions;

/// <summary>
/// Provides the set of trusted publisher public keys.
/// Loaded once at host startup from PluginSecurityOptions.TrustedPublishersPath.
/// </summary>
public interface ITrustedPublisherRegistry
{
    /// <summary>Retrieve the public key entry for the given key ID. Returns null if not found.</summary>
    PluginSigningKey? GetPublicKey(string publicKeyId);

    /// <summary>All registered trusted publisher keys.</summary>
    IReadOnlyList<PluginSigningKey> GetAll();
}
```

### `SignatureVerificationResult`

```csharp
// src/MSOSync.Plugin/Signing/Models/SignatureVerificationResult.cs
namespace MSOSync.Plugin.Signing.Models;

public enum SignatureVerificationOutcome
{
    Valid,
    NoSignature,
    UnknownPublisher,
    InvalidBase64,
    InvalidSignature,
    UnsupportedAlgorithm
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
        => new(SignatureVerificationOutcome.UnknownPublisher, keyId, $"Public key ID '{keyId}' is not in the trusted publisher registry.");

    public static SignatureVerificationResult InvalidBase64(string keyId)
        => new(SignatureVerificationOutcome.InvalidBase64, keyId, "Signature value is not valid Base64.");

    public static SignatureVerificationResult InvalidSignature(string keyId)
        => new(SignatureVerificationOutcome.InvalidSignature, keyId, "Signature does not match the canonical manifest hash.");

    public static SignatureVerificationResult UnsupportedAlgorithm(string algorithm)
        => new(SignatureVerificationOutcome.UnsupportedAlgorithm, null, $"Signing algorithm '{algorithm}' is not supported. Expected 'Ed25519'.");
}
```

### `PluginSigningKey`

```csharp
// src/MSOSync.Plugin/Signing/Models/PluginSigningKey.cs
namespace MSOSync.Plugin.Signing.Models;

public sealed record PluginSigningKey
{
    /// <summary>Unique key identifier. Matches manifest.signature.publicKeyId.</summary>
    public string KeyId       { get; init; } = null!;

    /// <summary>Human-readable publisher name.</summary>
    public string Publisher   { get; init; } = null!;

    /// <summary>Base64-standard-encoded 32-byte Ed25519 public key.</summary>
    public string PublicKeyB64 { get; init; } = null!;

    /// <summary>ISO-8601 UTC datetime when this key was added to the registry.</summary>
    public string AddedAt     { get; init; } = null!;

    /// <summary>Optional ISO-8601 UTC expiry. Null = never expires.</summary>
    public string? ExpiresAt  { get; init; }
}
```

---

## Packaging Interface

### `IPluginPackager`

```csharp
// src/MSOSync.Plugin/Packaging/Abstractions/IPluginPackager.cs
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
    ///   Directory containing plugin.json (ManifestV2), the entry DLL, optional lib/, optional assets/.
    ///   Must exist and contain a valid manifest.json or plugin.json (both accepted as source).
    /// </param>
    /// <param name="outputPackagePath">
    ///   Full path where the .msopkg file will be written, including filename. Parent directory must exist.
    /// </param>
    /// <param name="signingKey">
    ///   If provided, the resulting archive is signed with this key.
    ///   The manifest inside the archive will include a populated signature block.
    ///   Pass null to produce an unsigned package (valid for local dev when
    ///   PluginSecurityOptions.RequireSignedPackages = false).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="PluginPackagingException">Thrown for any validation or IO failure.</exception>
    Task PackageAsync(
        string                   pluginSourceDirectory,
        string                   outputPackagePath,
        IPluginSigner?           signingKey,
        CancellationToken        ct);
}
```

---

## Installation Interface

### `IPluginInstaller`

```csharp
// src/MSOSync.Plugin/Packaging/Abstractions/IPluginInstaller.cs
namespace MSOSync.Plugin.Packaging.Abstractions;

/// <summary>
/// Installs a .msopkg archive into the configured plugins directory.
/// </summary>
public interface IPluginInstaller
{
    /// <summary>
    /// Install (or upgrade) the plugin packaged in <paramref name="packagePath"/>.
    ///
    /// The installer runs these steps in order:
    ///   1. Open archive and validate size/file count limits
    ///   2. Extract and parse manifest.json as ManifestV2
    ///   3. Validate manifest schema (same rules as ManifestV2Validator)
    ///   4. Check sdkVersionConstraint against host SDK version
    ///   5. Signature verification (per PluginSecurityOptions)
    ///   6. Verify SHA-256 of every entry in manifest.files[]
    ///   7. Unpack all archive entries to a temp directory
    ///   8. Write derived v1 plugin.json into temp directory (for PluginLoader compatibility)
    ///   9. Atomic rename temp dir → plugins/{pluginId}
    ///  10. Call IPluginStore.UpsertAsync with PackageHash, SignedBy, SignatureAlgorithm
    ///
    /// Returns PackageInstallResult.Ok on success or PackageInstallResult.Fail on any error.
    /// Never throws; all exceptions are caught and translated into a failure result.
    /// </summary>
    /// <param name="packagePath">Absolute path to the .msopkg file to install.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PackageInstallResult> InstallAsync(string packagePath, CancellationToken ct);

    /// <summary>
    /// Remove an installed plugin by ID. Deletes plugins/{pluginId} directory and
    /// calls IPluginStore.SetEnabledAsync(pluginId, false).
    /// Returns false if the plugin directory does not exist.
    /// </summary>
    Task<bool> UninstallAsync(string pluginId, CancellationToken ct);
}
```

---

## Signing Workflow

### Key Generation

Ed25519 keys are generated outside the MSOSync host. Publishers use the `msosync` CLI tool (Phase 2C.3, deferred) or any standard tool. For the purpose of this spec, key generation uses `System.Security.Cryptography.Ed25519` (available in .NET 9 via `ECDsa`-derived APIs) or the `NSec.Cryptography` pattern. MSOSync itself only stores and validates public keys.

Key generation pseudocode (for reference; not part of the installer):

```csharp
// .NET 9 does not ship a named "Ed25519" class by string.
// Use ECDiffieHellman-like approach through BouncyCastle or the
// System.Security.Cryptography.MLDsa-adjacent API path.
// In .NET 9 the canonical approach is:
var privateKey = new byte[32];
RandomNumberGenerator.Fill(privateKey);
// Derive public key using the Ed25519 specification or via a
// wrapper. For this spec, the concrete implementation is
// Ed25519PluginSigner which uses Microsoft.Extensions.Security
// patterns established in Phase 2E.
```

NOTE: .NET 9 does not expose a first-party `Ed25519` named class in `System.Security.Cryptography`. The implementation uses the `System.Security.Cryptography.ECDsa` curve `Oid("1.3.101.112")` (id-EdDSA) or, if unavailable in the target runtime, falls back to **RSA-PSS-2048** as the signing algorithm with `SHA-256` as the digest. The `PluginSecurityOptions.PreferredSigningAlgorithm` property (default `"Ed25519"`) controls which algorithm `IPluginSigner` uses. The verifier supports both `"Ed25519"` and `"RSA-PSS-SHA256"` as values for `manifest.signature.algorithm`.

RSA-PSS-2048 fallback keys are 2048-bit RSA key pairs. Private key stored in PKCS#8 PEM. Public key stored in SubjectPublicKeyInfo PEM.

For simplicity, this spec targets **RSA-PSS-SHA256** as the mandated initial algorithm with Ed25519 as an upgrade path when .NET exposes it cleanly, since `RSACryptoServiceProvider` and `RSA.Create()` are unambiguously available in .NET 9.

**Final mandated algorithm for 2C.1:** `RSA-PSS-SHA256` (2048-bit minimum key size). The `algorithm` field in the signature block must be `"RSA-PSS-SHA256"`. Ed25519 support is added when the .NET runtime exposes it without third-party dependencies.

### `PluginSecurityOptions.PreferredSigningAlgorithm`

```csharp
/// <summary>Algorithm for IPluginSigner. Supported values: "RSA-PSS-SHA256". Default: "RSA-PSS-SHA256".</summary>
public string PreferredSigningAlgorithm { get; set; } = "RSA-PSS-SHA256";
```

### Canonical Manifest Hash

The canonical hash is the input to the signing and verification operations. It is computed as follows:

1. Serialize the `ManifestV2` with `Signature = null` (set the signature block to null before serialization).
2. Sort all JSON object properties alphabetically (use `JsonSerializerOptions` with a property naming policy that guarantees deterministic output — no indentation, no trailing whitespace, camelCase property names matching `[JsonPropertyName]` attributes, sorted by key name).
3. UTF-8 encode the resulting JSON string.
4. Compute SHA-256 over the UTF-8 bytes.
5. The resulting 32-byte hash is the input to `RSA.SignHash` (PSS, SHA-256) or Ed25519 sign.

Canonical serialization options used by both `IPluginPackager` and `IPluginSignatureVerifier`:

```csharp
private static readonly JsonSerializerOptions CanonicalOptions = new()
{
    WriteIndented              = false,
    PropertyNamingPolicy       = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition     = JsonIgnoreCondition.WhenWritingNull,
    Encoder                    = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
```

The canonical hash approach avoids signing the raw bytes of `manifest.json` from the archive (which could include formatting differences) and instead signs the normalized object model. This means the verifier re-serializes the parsed model. Both sides use identical serializer options.

### Signing Steps (Packager)

1. Build `ManifestV2` with all fields including `files[]` but `Signature = null`.
2. Serialize using canonical options → `string canonicalJson`.
3. Compute `byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))`.
4. Call `IPluginSigner.Sign(hash)` → returns Base64-encoded signature string.
5. Set `manifest = manifest with { Signature = new ManifestSignatureBlock { Algorithm = "RSA-PSS-SHA256", PublicKeyId = signer.PublicKeyId, Value = signature } }`.
6. Serialize final manifest (with signature block) into `manifest.json` inside the archive.

### Verification Steps (Installer)

1. Extract `manifest.json` raw bytes from archive. Decode as UTF-8 string → `rawJson`.
2. Deserialize into `ManifestV2 manifest`.
3. Check `manifest.Signature != null`. If null and `RequireSignedPackages = true` → fail with `NoSignature`.
4. If null and `RequireSignedPackages = false` → return `SignatureVerificationResult.NoSignature()` (allowed).
5. Check `manifest.Signature.Algorithm == "RSA-PSS-SHA256"` (case-insensitive). Otherwise → `UnsupportedAlgorithm`.
6. Look up `manifest.Signature.PublicKeyId` in `ITrustedPublisherRegistry`. If not found and `RequireTrustedPublisher = true` → `UnknownPublisher`.
7. Decode `manifest.Signature.Value` from Base64 → `sigBytes`. If invalid → `InvalidBase64`.
8. Build canonical: set `manifest = manifest with { Signature = null }`, serialize with canonical options → `canonicalJson`, compute SHA-256.
9. Call `RSA.VerifyHash(hash, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)` with the public key from step 6.
10. Return `Valid` or `InvalidSignature`.

### Trusted Publishers File — `trusted-publishers.json`

Loaded once at host startup by `TrustedPublisherRegistry`. Location: `PluginSecurityOptions.TrustedPublishersPath` (default: `trusted-publishers.json` relative to `AppContext.BaseDirectory`).

```json
{
  "publishers": [
    {
      "keyId": "msosync-official-2024-01",
      "publisher": "MSOSync Official",
      "publicKeyB64": "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA...base64-encoded-DER-SubjectPublicKeyInfo...",
      "addedAt": "2024-01-15T00:00:00Z",
      "expiresAt": null
    },
    {
      "keyId": "acme-corp-2024-01",
      "publisher": "Acme Corp",
      "publicKeyB64": "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA...another-key...",
      "addedAt": "2024-03-01T00:00:00Z",
      "expiresAt": "2026-03-01T00:00:00Z"
    }
  ]
}
```

`publicKeyB64` is the Base64-standard-encoded DER-encoded SubjectPublicKeyInfo (SPKI) of the RSA-2048 public key. Loaded via `RSA.Create().ImportSubjectPublicKeyInfo(...)`.

`TrustedPublisherRegistry` at startup:
- Reads the file if it exists. If the file does not exist, logs a warning and initializes with an empty list (unsigned packages still work if `RequireSignedPackages = false`).
- Filters out entries where `expiresAt` is non-null and in the past. Logs `PluginSecurity2001` for each expired key that is skipped.
- Caches the result in a `Dictionary<string, PluginSigningKey>` for O(1) lookup.
- Is registered as a singleton.

---

## Installation Workflow — Detailed Steps

### Stage 1: Archive Validation

- Open `packagePath` with `ZipFile.OpenRead`.
- Count total entries. If > `PackagingOptions.MaxFileCount` → fail(`ArchiveValidation`, "Archive exceeds maximum file count.").
- Sum uncompressed sizes of all entries. If > `PackagingOptions.MaxPackageSizeBytes` → fail(`ArchiveValidation`, "Archive exceeds maximum uncompressed size.").
- Verify exactly one entry named `manifest.json` exists at archive root (no subdirectory prefix) → fail if absent (`ArchiveValidation`, "manifest.json not found in archive root.").

### Stage 2: Manifest Parse

- Read `manifest.json` entry into memory (limit: `PluginHostOptions.MaxManifestSizeBytes`, default 64 KB).
- Deserialize as `ManifestV2` with `PropertyNameCaseInsensitive = true`.
- On `JsonException` → fail(`ManifestParse`, message).

### Stage 3: Manifest Schema Validation

Validates using `ManifestV2Validator.Validate(manifest)`:

- `manifestVersion` must be `2`.
- `id`, `name`, `version`, `sdkVersion`, `sdkVersionConstraint`, `apiVersion`, `entryAssembly`, `entryType`, `author`, `description` must be non-null and non-whitespace.
- `id` must not contain path separators or `..`.
- `version` must be parseable as `System.Version`.
- `entryAssembly` must not contain path separators or `..`.
- `entryType` must not contain path separators or `..`.
- `files[]` must be non-empty.
- Each `files[].path` must not contain `..` and must not be absolute.
- Each `files[].sha256` must be exactly 64 lowercase hex characters.
- No duplicate `files[].path` entries.
- `pluginDependencies[].id` must be non-null and non-whitespace.
- `pluginDependencies[].versionRange` must parse as a valid semver range (see `SdkVersionConstraintParser`).
- `keywords` must have ≤ 10 entries.
- Returns first error string or null on success.

### Stage 4: SDK Version Constraint Check

- Parse `manifest.sdkVersionConstraint` using `SdkVersionConstraintParser.Parse`.
- Resolve host SDK version from `PluginHostOptions.SupportedSdkMajorVersion` + `.0.0`.
- Evaluate constraint: if host SDK version does not satisfy the range → fail(`SdkVersionConstraint`, "Host SDK version {v} does not satisfy constraint '{c}'.").

### `SdkVersionConstraintParser`

Supports a minimal subset of npm-style semver ranges:
- `>=X.Y.Z` — greater than or equal
- `>X.Y.Z` — strictly greater
- `<=X.Y.Z` — less than or equal
- `<X.Y.Z` — strictly less
- `=X.Y.Z` or bare `X.Y.Z` — exact match
- `>=X.Y.Z <A.B.C` — range (space-separated AND of two comparators)

```csharp
// src/MSOSync.Plugin/Packaging/SdkVersionConstraintParser.cs
namespace MSOSync.Plugin.Packaging;

public static class SdkVersionConstraintParser
{
    /// <summary>
    /// Parse the constraint string into a predicate.
    /// Returns null if the constraint string is invalid.
    /// </summary>
    public static Func<Version, bool>? Parse(string constraint);

    /// <summary>
    /// Returns true if <paramref name="hostVersion"/> satisfies <paramref name="constraint"/>.
    /// Returns false if the constraint is unparseable (treat as incompatible).
    /// </summary>
    public static bool Satisfies(string constraint, Version hostVersion);
}
```

### Stage 5: Signature Verification

- Call `IPluginSignatureVerifier.Verify(manifest, rawManifestJson)`.
- If `RequireSignedPackages = true` and result is not `Valid` → fail(`SignatureVerification`, result.ErrorMessage).
- If `RequireSignedPackages = false`:
  - If `manifest.Signature != null` and result is not `Valid` → fail(`SignatureVerification`, result.ErrorMessage). (A present-but-invalid signature always fails, even in dev mode.)
  - If `manifest.Signature == null` → allowed; continue.
- Log `PluginSecurity2002` if signature is absent and `RequireSignedPackages = false`.

### Stage 6: File Hash Verification

- For each entry in `manifest.files[]`:
  - Find the corresponding entry in the archive by path.
  - If not found → fail(`HashVerification`, "File '{path}' listed in manifest.files[] not found in archive.").
  - Open the archive entry stream, compute SHA-256 incrementally (4 KB buffer, do not load entire file into memory).
  - Compare hex string (lowercase) to `manifest.files[n].sha256`.
  - If mismatch → fail(`HashVerification`, "Hash mismatch for '{path}'. Expected '{expected}', computed '{actual}'.").
- Log `PluginSecurity2003` (hash verification complete, N files verified).

### Stage 7: Unpack to Temp Directory

- Create temp directory: `Path.Combine(Path.GetTempPath(), $"msopkg-{manifest.Id}-{Guid.NewGuid():N}")`.
- Extract all entries from the archive to the temp directory.
- For `assets/` entries: enforce `MaxAssetsFileCount` and `MaxAssetsSizeBytes` limits, fail if exceeded.
- Validate no extracted path escapes the temp directory (path traversal guard: canonicalize each destination path and verify it starts with `canonicalTempDir`).

### Stage 8: Write Derived v1 `plugin.json`

The existing `PluginLoader` reads `plugin.json` (v1 manifest). The installer writes a derived v1 `plugin.json` into the temp directory so the plugin is loadable by the existing pipeline on next host restart.

```csharp
var v1 = new PluginManifest
{
    ManifestVersion = 1,
    Id              = manifest.Id,
    Name            = manifest.Name,
    Version         = manifest.Version,
    SdkVersion      = manifest.SdkVersion,
    ApiVersion      = manifest.ApiVersion,
    StartupOrder    = manifest.StartupOrder,
    MinHostVersion  = manifest.MinHostVersion,
    MaxHostVersion  = manifest.MaxHostVersion,
    EntryAssembly   = manifest.EntryAssembly,
    EntryType       = manifest.EntryType,
    Author          = manifest.Author,
    Description     = manifest.Description,
    Permissions     = manifest.Permissions.ToList(),
    Dependencies    = manifest.PluginDependencies.Select(d => d.Id).ToList(),
    Capabilities    = manifest.Capabilities.ToList(),
};
```

Write as `plugin.json` (not `manifest.json`) into the temp directory. Both files co-exist in the installed plugin directory. `PluginLoader` reads `plugin.json` and ignores `manifest.json`.

### Stage 9: Atomic Move

- Destination: `Path.Combine(PluginHostOptions.PluginsPath, manifest.Id)`.
- If destination exists (upgrade scenario):
  - Rename existing directory to `{manifest.Id}.bak.{timestamp}` as rollback copy.
  - Move temp directory to destination.
  - Delete `.bak` directory after successful move.
  - If move fails: attempt to restore `.bak` directory → log `PluginInstall3002` (rollback attempted).
- If destination does not exist: `Directory.Move(tempDir, destination)`.
- On any IO exception during move → fail(`AtomicMove`, exception.Message). Clean up temp directory.

### Stage 10: Persist to Store

```csharp
var record = new PluginRecord
{
    PluginId           = manifest.Id,
    PluginName         = manifest.Name,
    PluginVersion      = manifest.Version,
    Status             = PluginStatus.Loaded.ToString(),
    Enabled            = true,
    InstalledAt        = DateTime.UtcNow,
    LastSeenAt         = DateTime.UtcNow,
    LastError          = null,
    ManifestHash       = ComputePackageHash(packagePath),  // SHA-256 of .msopkg file
    HostVersion        = options.Value.HostVersion,
    // Extended columns (see Database Schema below):
    PackageHash        = ComputePackageHash(packagePath),
    SignedBy           = manifest.Signature?.PublicKeyId,
    SignatureAlgorithm = manifest.Signature?.Algorithm,
    IsPackageInstall   = true,
};
await store.UpsertAsync(record, ct);
```

Return `PackageInstallResult.Ok(manifest.Id, manifest.Version)`.

---

## Database Schema Extension

### Extended `SyncPlugin` Columns

No new migration is required. The four columns are added as nullable `nvarchar` / `bit` columns appended to the existing `sync_plugin` table. This follows the constraint: no new migrations unless strictly necessary.

The columns are added via a single `ALTER TABLE` migration appended to the existing migration file or as a new lightweight migration `M030_PluginPackagingColumns`. Given that new migrations are permitted when strictly necessary, `M030_PluginPackagingColumns` is the correct path — it adds four columns to one table and is minimal.

```sql
-- M030_PluginPackagingColumns
ALTER TABLE msosync.sync_plugin
    ADD package_hash        nvarchar(64)  NULL,
        signed_by           nvarchar(200) NULL,
        signature_algorithm nvarchar(50)  NULL,
        is_package_install  bit           NOT NULL DEFAULT 0;
```

Updated `SyncPlugin` entity:

```csharp
// src/MSOSync.Persistence/Entities/SyncPlugin.cs (extended)
[GlobalEntity]
public sealed class SyncPlugin
{
    public string   PluginId           { get; set; } = null!;
    public string   PluginName         { get; set; } = null!;
    public string   PluginVersion      { get; set; } = null!;
    public string   Status             { get; set; } = null!;
    public bool     Enabled            { get; set; } = true;
    public DateTime InstalledAt        { get; set; }
    public DateTime LastSeenAt         { get; set; }
    public string?  LastError          { get; set; }
    public string?  ManifestHash       { get; set; }
    public string?  HostVersion        { get; set; }
    // 2C.1 additions:
    public string?  PackageHash        { get; set; }   // SHA-256 of the .msopkg file
    public string?  SignedBy           { get; set; }   // publicKeyId from signature block, null if unsigned
    public string?  SignatureAlgorithm { get; set; }   // "RSA-PSS-SHA256" or null if unsigned
    public bool     IsPackageInstall   { get; set; }   // true = installed via .msopkg, false = directory-based
}
```

Updated `PluginRecord` model (mirrors `SyncPlugin`):

```csharp
// src/MSOSync.Plugin/Models/PluginRecord.cs (extended)
public sealed class PluginRecord
{
    public string   PluginId           { get; set; } = null!;
    public string   PluginName         { get; set; } = null!;
    public string   PluginVersion      { get; set; } = null!;
    public string   Status             { get; set; } = null!;
    public bool     Enabled            { get; set; } = true;
    public DateTime InstalledAt        { get; set; }
    public DateTime LastSeenAt         { get; set; }
    public string?  LastError          { get; set; }
    public string?  ManifestHash       { get; set; }
    public string?  HostVersion        { get; set; }
    // 2C.1 additions:
    public string?  PackageHash        { get; set; }
    public string?  SignedBy           { get; set; }
    public string?  SignatureAlgorithm { get; set; }
    public bool     IsPackageInstall   { get; set; }
}
```

The `IPluginStore` interface gains two new methods to support package-specific queries:

```csharp
// additions to IPluginStore
Task<PluginRecord?> GetByIdAsync(string pluginId, CancellationToken ct);
Task DeleteAsync(string pluginId, CancellationToken ct);  // used by IPluginInstaller.UninstallAsync
```

Both implementations in `MSOSync.Persistence` use `AsNoTracking()` on all reads. No lazy loading.

---

## Error Handling

### Error Matrix

| Failure Scenario | Stage | `PackageInstallResult.FailureStage` | Recoverable? |
|---|---|---|---|
| File does not exist or is not a valid ZIP | `ArchiveValidation` | `ArchiveValidation` | Fix the package file |
| Archive exceeds size or file count limits | `ArchiveValidation` | `ArchiveValidation` | Reduce package size |
| `manifest.json` absent from archive root | `ArchiveValidation` | `ArchiveValidation` | Fix the package |
| `manifest.json` is malformed JSON | `ManifestParse` | `ManifestParse` | Fix the manifest |
| Required manifest field missing or invalid | `ManifestValidation` | `ManifestValidation` | Fix the manifest |
| `manifestVersion != 2` | `ManifestValidation` | `ManifestValidation` | Repackage with v2 manifest |
| Host SDK version outside `sdkVersionConstraint` | `SdkVersionConstraint` | `SdkVersionConstraint` | Update plugin or host |
| `RequireSignedPackages = true` and no signature | `SignatureVerification` | `SignatureVerification` | Sign the package |
| Signature present but algorithm unsupported | `SignatureVerification` | `SignatureVerification` | Re-sign with RSA-PSS-SHA256 |
| Publisher not in trusted registry | `SignatureVerification` | `SignatureVerification` | Add publisher key or use a trusted key |
| Signature cryptographically invalid | `SignatureVerification` | `SignatureVerification` | Re-sign; possible tampering |
| File listed in `manifest.files[]` absent from archive | `HashVerification` | `HashVerification` | Rebuild package |
| SHA-256 mismatch on a DLL or config file | `HashVerification` | `HashVerification` | Possible tampering; rebuild from source |
| Path traversal attempt in archive entry | `Unpack` | `Unpack` | Discard package; report to publisher |
| Asset count or size limit exceeded | `Unpack` | `Unpack` | Reduce assets |
| IO failure during atomic move | `AtomicMove` | `AtomicMove` | Check disk permissions; retry |
| Store `UpsertAsync` fails | `StorePersist` | `StorePersist` | Transient DB error; retry |

### Exception Type

```csharp
// src/MSOSync.Plugin/Packaging/PluginPackagingException.cs
namespace MSOSync.Plugin.Packaging;

public sealed class PluginPackagingException : Exception
{
    public string Stage { get; }

    public PluginPackagingException(string stage, string message)
        : base($"[{stage}] {message}")
        => Stage = stage;

    public PluginPackagingException(string stage, string message, Exception inner)
        : base($"[{stage}] {message}", inner)
        => Stage = stage;
}
```

`IPluginPackager` throws `PluginPackagingException`. `IPluginInstaller.InstallAsync` catches all exceptions and returns `PackageInstallResult.Fail`.

### Logging Event IDs

| ID | Event | Level |
|---|---|---|
| `PluginSecurity2001` | Expired trusted publisher key skipped | Warning |
| `PluginSecurity2002` | Unsigned package accepted (dev mode) | Information |
| `PluginSecurity2003` | Hash verification complete (N files) | Debug |
| `PluginInstall3001` | Package installation started | Information |
| `PluginInstall3002` | Rollback attempted after AtomicMove failure | Warning |
| `PluginInstall3003` | Package installation succeeded | Information |
| `PluginInstall3004` | Package installation failed (stage + error) | Warning |
| `PluginInstall3005` | Plugin uninstalled | Information |

---

## Testing Approach

### Unit Tests — `tests/MSOSync.PluginTests/`

#### `Packaging/ManifestV2ValidatorTests.cs`

| Test | Scenario |
|---|---|
| `Validate_ValidManifest_ReturnsNull` | All required fields present → null (success) |
| `Validate_MissingId_ReturnsError` | `id` absent → error message contains "id" |
| `Validate_ManifestVersionNot2_ReturnsError` | `manifestVersion: 1` → validation error |
| `Validate_InvalidVersionFormat_ReturnsError` | `version: "not-a-version"` → error |
| `Validate_PathTraversalInEntryAssembly_ReturnsError` | `entryAssembly: "../evil.dll"` → error |
| `Validate_DuplicateFilePaths_ReturnsError` | Two `files[]` entries with same path → error |
| `Validate_InvalidSha256Length_ReturnsError` | `sha256` with 63 chars → error |
| `Validate_InvalidSha256NotHex_ReturnsError` | `sha256` with non-hex chars → error |
| `Validate_TooManyKeywords_ReturnsError` | 11 keywords → error |
| `Validate_InvalidVersionRange_ReturnsError` | `pluginDependencies[].versionRange: "##invalid"` → error |

#### `Packaging/SdkVersionConstraintParserTests.cs`

| Test | Scenario |
|---|---|
| `Parse_GreaterThanOrEqual_Satisfied` | `">=1.0.0"`, host `1.2.0` → true |
| `Parse_StrictLessThan_Satisfied` | `"<2.0.0"`, host `1.9.9` → true |
| `Parse_StrictLessThan_NotSatisfied` | `"<2.0.0"`, host `2.0.0` → false |
| `Parse_Range_Satisfied` | `">=1.0.0 <2.0.0"`, host `1.5.0` → true |
| `Parse_Range_ExactLowerBound_Satisfied` | `">=1.0.0 <2.0.0"`, host `1.0.0` → true |
| `Parse_Range_UpperBoundExclusive` | `">=1.0.0 <2.0.0"`, host `2.0.0` → false |
| `Parse_ExactMatch_Satisfied` | `"=1.0.0"`, host `1.0.0` → true |
| `Parse_ExactMatch_NotSatisfied` | `"=1.0.0"`, host `1.0.1` → false |
| `Parse_InvalidConstraint_ReturnsFalse` | `"banana"` → `Satisfies` returns false |

#### `Signing/Ed25519SignerTests.cs` (RSA-PSS in 2C.1)

| Test | Scenario |
|---|---|
| `Sign_ProducesBase64EncodedOutput` | Signing arbitrary bytes produces valid Base64 |
| `Sign_SameInputProducesVerifiableOutput` | Signed bytes can be verified with corresponding public key |
| `Sign_PublicKeyId_MatchesConstructorValue` | `signer.PublicKeyId` returns the key ID passed at construction |

#### `Signing/SignatureVerifierTests.cs`

| Test | Scenario |
|---|---|
| `Verify_ValidSignature_ReturnsValid` | Correctly signed manifest → `Valid` |
| `Verify_NoSignatureBlock_ReturnsNoSignature` | `manifest.Signature == null` → `NoSignature` |
| `Verify_UnknownPublisher_ReturnsUnknownPublisher` | `publicKeyId` not in registry → `UnknownPublisher` |
| `Verify_InvalidBase64_ReturnsInvalidBase64` | `value` field is not valid Base64 → `InvalidBase64` |
| `Verify_TamperedManifest_ReturnsInvalidSignature` | Modify manifest field after signing → `InvalidSignature` |
| `Verify_UnsupportedAlgorithm_ReturnsUnsupportedAlgorithm` | `algorithm: "Ed448"` → `UnsupportedAlgorithm` |
| `Verify_ExpiredKey_TreatedAsUnknownPublisher` | Expired key filtered at registry load → `UnknownPublisher` |

#### `Packaging/PluginPackagerTests.cs`

| Test | Scenario |
|---|---|
| `PackageAsync_ValidSourceDir_CreatesZipFile` | Valid source dir + unsigned → `.msopkg` file created on disk |
| `PackageAsync_MissingManifest_ThrowsPackagingException` | No `plugin.json` or `manifest.json` in source dir → `PluginPackagingException` |
| `PackageAsync_WithSigningKey_ManifestContainsSignatureBlock` | Signed package → `manifest.json` inside archive has `signature` block |
| `PackageAsync_FileHashesInManifest_MatchActualFiles` | Hashes in `files[]` match SHA-256 of files in archive |
| `PackageAsync_OutputExtensionIsMsopkg` | Output path extension is `.msopkg` |
| `PackageAsync_EntryAssemblyNotFound_ThrowsPackagingException` | `entryAssembly` file missing → exception at stage `ManifestValidation` |

#### `Packaging/PluginInstallerTests.cs`

| Test | Scenario |
|---|---|
| `InstallAsync_ValidUnsignedPackage_DevMode_Succeeds` | Unsigned package, `RequireSignedPackages = false` → `Success = true`, plugin dir created |
| `InstallAsync_ValidSignedPackage_Succeeds` | Signed package, publisher in registry → `Success = true` |
| `InstallAsync_UnsignedPackage_RequireSignedPackages_Fails` | Unsigned, `RequireSignedPackages = true` → fail at `SignatureVerification` |
| `InstallAsync_TamperedDll_HashMismatch_Fails` | DLL modified after hashing → fail at `HashVerification` |
| `InstallAsync_InvalidSignature_Fails` | Signature doesn't match canonical hash → fail at `SignatureVerification` |
| `InstallAsync_IncompatibleSdkVersion_Fails` | `sdkVersionConstraint: ">=99.0.0"` → fail at `SdkVersionConstraint` |
| `InstallAsync_ArchiveTooLarge_Fails` | Archive with 201 files → fail at `ArchiveValidation` |
| `InstallAsync_PathTraversalInArchive_Fails` | Entry with `../evil` path → fail at `Unpack` |
| `InstallAsync_Upgrade_ReplacesExistingPluginDirectory` | Install same plugin ID twice → second install replaces first |
| `InstallAsync_StoreUpsertCalled_WithPackageHashAndSignedBy` | Verify `IPluginStore.UpsertAsync` called with correct `PackageHash` and `SignedBy` |
| `UninstallAsync_ExistingPlugin_RemovesDirectory` | Plugin dir exists → removed, `SetEnabledAsync(false)` called |
| `UninstallAsync_NonExistentPlugin_ReturnsFalse` | Plugin dir does not exist → returns `false`, no exception |

### Integration Tests

Integration tests use real ZIP archives built in-memory (`ZipArchive` over `MemoryStream`) and a real `IPluginStore` backed by an in-memory EF Core `DbContext` (same pattern as existing `MSOSync.IntegrationTests`).

| Test | Scenario |
|---|---|
| `PackageThenInstall_RoundTrip_PluginIsLoadable` | `IPluginPackager` creates archive → `IPluginInstaller` installs → `PluginLoader.LoadAllAsync` on the plugins dir → `PluginLoadResult.Success` |
| `PackageSignInstall_ValidPublisher_LoadsAsVerified` | Full signed round-trip; verify `SyncPlugin.SignedBy` is persisted |
| `InstallUpgrade_OldVersionReplaced_StoreContainsNewVersion` | Install v1 then v2 of same plugin ID → store `PluginVersion` is `v2` |

---

## DI Registration

```csharp
// In MSOSync.App / MSOSync.Plugin service extensions

services.Configure<PluginSecurityOptions>(configuration.GetSection("PluginSecurity"));
services.Configure<PackagingOptions>(configuration.GetSection("PluginPackaging"));

services.AddSingleton<ITrustedPublisherRegistry, TrustedPublisherRegistry>();
services.AddSingleton<IPluginSignatureVerifier, RsaPssSignatureVerifier>();
services.AddScoped<IPluginPackager, PluginPackager>();
services.AddScoped<IPluginInstaller, PluginInstaller>();
```

`TrustedPublisherRegistry` is a singleton because it reads the JSON file once at startup.
`RsaPssSignatureVerifier` is a singleton; it holds the loaded public keys via `ITrustedPublisherRegistry`.
`PluginPackager` and `PluginInstaller` are scoped because they perform per-request IO operations.

---

## Configuration (`appsettings.json`)

```json
{
  "PluginSecurity": {
    "RequireSignedPackages": false,
    "RequireTrustedPublisher": true,
    "TrustedPublishersPath": "trusted-publishers.json",
    "PreferredSigningAlgorithm": "RSA-PSS-SHA256"
  },
  "PluginPackaging": {
    "MaxPackageSizeBytes": 52428800,
    "MaxFileCount": 200,
    "MaxAssetsSizeBytes": 2097152,
    "MaxAssetsFileCount": 20
  }
}
```

---

## File Paths — New Files

| Path | Purpose |
|---|---|
| `src/MSOSync.Plugin/Packaging/Abstractions/IPluginPackager.cs` | Packager interface |
| `src/MSOSync.Plugin/Packaging/Abstractions/IPluginInstaller.cs` | Installer interface |
| `src/MSOSync.Plugin/Packaging/Models/ManifestV2.cs` | Manifest v2 record |
| `src/MSOSync.Plugin/Packaging/Models/ManifestSignatureBlock.cs` | Signature block record |
| `src/MSOSync.Plugin/Packaging/Models/PackageFileEntry.cs` | File hash entry record |
| `src/MSOSync.Plugin/Packaging/Models/PluginDependencyEntry.cs` | Dependency entry record |
| `src/MSOSync.Plugin/Packaging/Models/PackagingOptions.cs` | Options class |
| `src/MSOSync.Plugin/Packaging/Models/PackageInstallResult.cs` | Install result record |
| `src/MSOSync.Plugin/Packaging/ManifestV2Validator.cs` | Static validator |
| `src/MSOSync.Plugin/Packaging/SdkVersionConstraintParser.cs` | Constraint parser |
| `src/MSOSync.Plugin/Packaging/PluginPackagingException.cs` | Exception type |
| `src/MSOSync.Plugin/Packaging/Packager/PluginPackager.cs` | Packager implementation |
| `src/MSOSync.Plugin/Packaging/Installer/PluginInstaller.cs` | Installer implementation |
| `src/MSOSync.Plugin/Signing/Abstractions/IPluginSigner.cs` | Signer interface |
| `src/MSOSync.Plugin/Signing/Abstractions/IPluginSignatureVerifier.cs` | Verifier interface |
| `src/MSOSync.Plugin/Signing/Abstractions/ITrustedPublisherRegistry.cs` | Registry interface |
| `src/MSOSync.Plugin/Signing/Models/PluginSigningKey.cs` | Public key entry |
| `src/MSOSync.Plugin/Signing/Models/SignatureVerificationResult.cs` | Verification result |
| `src/MSOSync.Plugin/Signing/RsaPssPluginSigner.cs` | RSA-PSS signer |
| `src/MSOSync.Plugin/Signing/RsaPssSignatureVerifier.cs` | RSA-PSS verifier |
| `src/MSOSync.Plugin/Signing/TrustedPublisherRegistry.cs` | Registry implementation |
| `src/MSOSync.Plugin/Security/PluginSecurityOptions.cs` | Security options |
| `src/MSOSync.Persistence/Migrations/M030_PluginPackagingColumns.cs` | DB migration |
| `tests/MSOSync.PluginTests/Packaging/ManifestV2ValidatorTests.cs` | Unit tests |
| `tests/MSOSync.PluginTests/Packaging/SdkVersionConstraintParserTests.cs` | Unit tests |
| `tests/MSOSync.PluginTests/Packaging/PluginPackagerTests.cs` | Unit tests |
| `tests/MSOSync.PluginTests/Packaging/PluginInstallerTests.cs` | Unit tests |
| `tests/MSOSync.PluginTests/Signing/SignatureVerifierTests.cs` | Unit tests |
| `tests/MSOSync.PluginTests/Signing/RsaPssSignerTests.cs` | Unit tests |

---

## Global Constraints

| Constraint | Rule |
|---|---|
| No breaking changes | `IPlugin`, `IPluginContext`, `PluginManifest` (v1) are unchanged. All new types are additive. |
| Signing optional for local dev | `PluginSecurityOptions.RequireSignedPackages = false` allows unsigned packages. A present-but-invalid signature always fails regardless of this setting. |
| Signing required for marketplace | Any package served by the marketplace API (Phase 2C.2) must have `RequireSignedPackages = true` in that environment's configuration. |
| No lazy loading | All EF Core reads use `AsNoTracking()`. No navigation properties on `SyncPlugin`. |
| No third-party packages | `System.IO.Compression`, `System.Security.Cryptography`, `System.Text.Json` only. |
| xUnit + FluentAssertions | All tests use these frameworks, consistent with the existing test suite. |
| No memory buffering of full DLLs | Hash verification reads files via streaming (4 KB buffer) using `IncrementalHash`. |
| Path traversal guards everywhere | All archive entry paths and all file hash entry paths are checked against `..` and absolute paths before use. |
| Temp directory cleanup | `PluginInstaller` cleans up the temp directory on failure (in a `finally` block). |
| Atomic upgrade | Existing plugin directory is renamed to `.bak` before move. On failure, `.bak` is restored. On success, `.bak` is deleted. |
| Migration numbering | New migration is `M030_PluginPackagingColumns`. Update `PersistenceTests` table count assertion accordingly. |
| Test coverage | All new public interfaces, all validation branches, all error paths must have a dedicated unit test. |
