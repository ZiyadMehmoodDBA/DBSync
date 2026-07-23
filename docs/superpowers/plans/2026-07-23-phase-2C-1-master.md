# Phase 2C.1 — Plugin Packaging & Signing: Master Plan

**Date:** 2026-07-23
**Spec:** `docs/superpowers/specs/2026-07-23-phase-2C-1-plugin-packaging-signing.md`
**Status:** Ready to execute

---

## Goal

Implement a standardized, signed `.msopkg` package format for MSOSync plugins: manifest v2 schema, RSA-PSS-SHA256 signing and verification, tamper-detection via file hashes, `IPluginPackager`, and `IPluginInstaller`. No breaking changes to `IPlugin`, `IPluginContext`, or `PluginManifest` (v1).

---

## Task List

| # | Task File | Description | Depends On |
|---|-----------|-------------|------------|
| 1 | [task-1-manifest-packager](2026-07-23-phase-2C-1-task-1-manifest-packager.md) | ManifestV2 models, `SdkVersionConstraintParser`, `ManifestV2Validator`, `IPluginPackager` + `PluginPackager` implementation | — |
| 2 | [task-2-signing](2026-07-23-phase-2C-1-task-2-signing.md) | Signing models, `IPluginSigner`, `IPluginSignatureVerifier`, `ITrustedPublisherRegistry`, RSA-PSS implementations, `PluginSecurityOptions` | — (parallel with T1) |
| 3 | [task-3-installer](2026-07-23-phase-2C-1-task-3-installer.md) | `IPluginInstaller` + `PluginInstaller`, M036 migration, `SyncPlugin` + `PluginRecord` + `IPluginStore` extensions | T1 + T2 |
| 4 | [task-4-wiring-tests](2026-07-23-phase-2C-1-task-4-wiring-tests.md) | DI wiring in `PluginServiceExtensions`, integration tests (pack → sign → verify round-trip, tamper detection, unsigned-dev mode) | T1 + T2 + T3 |

---

## Execution Order

```
T1 ──┐
     ├──► T3 ──► T4
T2 ──┘
```

T1 and T2 are independent and can be executed in parallel.
T3 requires both T1 and T2 to be complete.
T4 requires T1, T2, and T3 to be complete.

---

## New Files Created

### `src/MSOSync.Plugin/`

```
Packaging/
  Abstractions/
    IPluginPackager.cs
    IPluginInstaller.cs
  Models/
    ManifestV2.cs
    ManifestSignatureBlock.cs
    PackageFileEntry.cs
    PluginDependencyEntry.cs
    PackagingOptions.cs
    PackageInstallResult.cs
  ManifestV2Validator.cs
  SdkVersionConstraintParser.cs
  PluginPackagingException.cs
  Packager/
    PluginPackager.cs
  Installer/
    PluginInstaller.cs
Signing/
  Abstractions/
    IPluginSigner.cs
    IPluginSignatureVerifier.cs
    ITrustedPublisherRegistry.cs
  Models/
    PluginSigningKey.cs
    SignatureVerificationResult.cs
  RsaPssPluginSigner.cs
  RsaPssSignatureVerifier.cs
  TrustedPublisherRegistry.cs
Security/
  PluginSecurityOptions.cs
```

### `src/MSOSync.Persistence/`

```
Entities/SyncPlugin.cs                     (extended — 4 new columns)
Migrations/M036_PluginPackagingColumns.cs  (new)
```

### `src/MSOSync.Plugin/` (modified)

```
Models/PluginRecord.cs                     (extended — 4 new properties)
Abstractions/IPluginStore.cs               (extended — GetByIdAsync + DeleteAsync)
Hosting/PluginServiceExtensions.cs         (extended — register packaging + signing services)
```

### `tests/MSOSync.PluginTests/`

```
Packaging/
  ManifestV2ValidatorTests.cs
  SdkVersionConstraintParserTests.cs
  PluginPackagerTests.cs
  PluginInstallerTests.cs
Signing/
  RsaPssSignerTests.cs
  SignatureVerifierTests.cs
Integration/
  PackageSignInstallTests.cs
```

---

## Global Constraints

- C# 13 / .NET 9 / `System.IO.Compression` / `System.Security.Cryptography` (RSA-PSS-SHA256). No new NuGet packages.
- No breaking changes to `IPlugin`, `IPluginContext`, `PluginManifest` (v1).
- Signing optional for local dev (`PluginSecurityOptions.RequireSignedPackages = false`). A present-but-invalid signature always fails regardless of this setting.
- All EF Core reads: `AsNoTracking()`. No lazy loading. No navigation properties on `SyncPlugin`.
- xUnit + FluentAssertions. Moq where needed.
- `MSOSync.Plugin.Packaging` and `MSOSync.Plugin.Signing` depend only on `MSOSync.Common` and `MSOSync.Sdk`.
- Neither namespace references `MSOSync.Persistence` directly; installer calls `IPluginStore` through the existing abstraction in `MSOSync.Plugin.Abstractions`.
- Git: stage files by name, never `git add .`, never commit `.env`.
- Hash verification reads via streaming (4 KB buffer) using `IncrementalHash` — no full-DLL buffering.
- Migration number: **M036_PluginPackagingColumns** (M030–M034 already exist; M035 reserved for another task).

---

## Logging Event ID Registry (2C.1 allocations)

| ID | Event | Level |
|----|-------|-------|
| `PluginSecurity2001` | Expired trusted publisher key skipped at registry load | Warning |
| `PluginSecurity2002` | Unsigned package accepted in dev mode (`RequireSignedPackages = false`) | Information |
| `PluginSecurity2003` | Hash verification complete (N files verified) | Debug |
| `PluginInstall3001` | Package installation started | Information |
| `PluginInstall3002` | Rollback attempted after `AtomicMove` failure | Warning |
| `PluginInstall3003` | Package installation succeeded | Information |
| `PluginInstall3004` | Package installation failed (stage + error) | Warning |
| `PluginInstall3005` | Plugin uninstalled | Information |
