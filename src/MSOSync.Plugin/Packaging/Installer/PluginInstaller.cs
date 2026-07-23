using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Packaging.Abstractions;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Security;
using MSOSync.Plugin.Signing.Abstractions;

namespace MSOSync.Plugin.Packaging.Installer;

internal sealed class PluginInstaller(
    IPluginStore                    store,
    IPluginSignatureVerifier        verifier,
    IOptions<PluginSecurityOptions> securityOptions,
    IOptions<PluginHostOptions>     hostOptions,
    IOptions<PackagingOptions>      packagingOptions,
    ILogger<PluginInstaller>        logger) : IPluginInstaller
{
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions V1WriteOpts = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly EventId InstallStarted    = new(3001, "PluginInstall3001");
    private static readonly EventId RollbackAttempted = new(3002, "PluginInstall3002");
    private static readonly EventId InstallSucceeded  = new(3003, "PluginInstall3003");
    private static readonly EventId InstallFailed     = new(3004, "PluginInstall3004");
    private static readonly EventId PluginUninstalled = new(3005, "PluginInstall3005");
    private static readonly EventId UnsignedAccepted  = new(2002, "PluginSecurity2002");
    private static readonly EventId HashVerifyDone    = new(2003, "PluginSecurity2003");

    public async Task<PackageInstallResult> InstallAsync(string packagePath, CancellationToken ct)
    {
        var sec     = securityOptions.Value;
        var host    = hostOptions.Value;
        var pkgOpts = packagingOptions.Value;

        logger.Log(LogLevel.Information, InstallStarted,
            "Package installation started: '{PackagePath}'", packagePath);

        string pluginId = "?";
        string tempDir  = string.Empty;

        try
        {
            // ── Stage 1: Archive Validation ──────────────────────────────────────
            if (!File.Exists(packagePath))
                return Fail(pluginId, "ArchiveValidation", $"Package file not found: '{packagePath}'");

            ZipArchive zip;
            try
            {
                zip = ZipFile.OpenRead(packagePath);
            }
            catch (Exception ex)
            {
                return Fail(pluginId, "ArchiveValidation", $"Not a valid ZIP archive: {ex.Message}");
            }

            using (zip)
            {
                // file count limit
                if (zip.Entries.Count > pkgOpts.MaxFileCount)
                    return Fail(pluginId, "ArchiveValidation",
                        $"Archive exceeds maximum file count of {pkgOpts.MaxFileCount} (found {zip.Entries.Count}).");

                // uncompressed size limit
                var totalSize = zip.Entries.Sum(e => e.Length);
                if (totalSize > pkgOpts.MaxPackageSizeBytes)
                    return Fail(pluginId, "ArchiveValidation",
                        $"Archive exceeds maximum uncompressed size of {pkgOpts.MaxPackageSizeBytes} bytes.");

                // must contain manifest.json at root
                var manifestEntry = zip.GetEntry("manifest.json");
                if (manifestEntry is null)
                    return Fail(pluginId, "ArchiveValidation",
                        "manifest.json not found at archive root.");

                // ── Stage 2: Manifest Parse ──────────────────────────────────────
                string rawManifestJson;
                ManifestV2 manifest;
                try
                {
                    using var ms = new MemoryStream((int)Math.Min(manifestEntry.Length, host.MaxManifestSizeBytes));
                    await using (var es = manifestEntry.Open())
                        await es.CopyToAsync(ms, ct);
                    rawManifestJson = Encoding.UTF8.GetString(ms.ToArray());
                    manifest = JsonSerializer.Deserialize<ManifestV2>(rawManifestJson, ReadOpts)
                               ?? throw new JsonException("Manifest deserialized to null.");
                }
                catch (JsonException ex)
                {
                    return Fail(pluginId, "ManifestParse", ex.Message);
                }

                pluginId = manifest.Id ?? "?";

                // ── Stage 3: Schema Validation ───────────────────────────────────
                var validationError = ManifestV2Validator.Validate(manifest);
                if (validationError is not null)
                    return Fail(pluginId, "ManifestValidation", validationError);

                // ── Stage 4: SDK Version Constraint ──────────────────────────────
                var hostSdkVersion = new Version(int.Parse(host.SupportedSdkMajorVersion), 0, 0);
                if (!SdkVersionConstraintParser.Satisfies(manifest.SdkVersionConstraint, hostSdkVersion))
                    return Fail(pluginId, "SdkVersionConstraint",
                        $"Host SDK version {hostSdkVersion} does not satisfy constraint '{manifest.SdkVersionConstraint}'.");

                // ── Stage 5: Signature Verification ──────────────────────────────
                var sigResult = verifier.Verify(manifest, rawManifestJson);
                if (manifest.Signature is not null)
                {
                    // Signature present but invalid → always fail
                    if (!sigResult.IsValid)
                        return Fail(pluginId, "SignatureVerification", sigResult.ErrorMessage ?? "Signature invalid.");
                }
                else
                {
                    // No signature block
                    if (sec.RequireSignedPackages)
                        return Fail(pluginId, "SignatureVerification",
                            "Package has no signature block and RequireSignedPackages = true.");

                    // Log dev-mode acceptance
                    logger.Log(LogLevel.Information, UnsignedAccepted,
                        "Unsigned package accepted for plugin '{PluginId}' (RequireSignedPackages = false).", pluginId);
                }

                // ── Stage 6: File Hash Verification ──────────────────────────────
                foreach (var fileEntry in manifest.Files)
                {
                    var archiveFile = zip.GetEntry(fileEntry.Path.Replace('\\', '/'));
                    if (archiveFile is null)
                        return Fail(pluginId, "HashVerification",
                            $"File '{fileEntry.Path}' listed in manifest.files[] not found in archive.");

                    var actualHash = await ComputeEntryHashAsync(archiveFile, ct);
                    if (!string.Equals(actualHash, fileEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                        return Fail(pluginId, "HashVerification",
                            $"Hash mismatch for '{fileEntry.Path}'. Expected '{fileEntry.Sha256}', computed '{actualHash}'.");
                }

                logger.Log(LogLevel.Debug, HashVerifyDone,
                    "Hash verification complete for plugin '{PluginId}': {Count} files verified.",
                    pluginId, manifest.Files.Count);

                // ── Stage 7: Unpack to Temp Directory ────────────────────────────
                tempDir = Path.Combine(
                    Path.GetTempPath(),
                    $"msopkg-{manifest.Id}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                var canonicalTemp = Path.GetFullPath(tempDir);
                int  assetCount   = 0;
                long assetsSize   = 0;

                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

                    var destPath = Path.GetFullPath(
                        Path.Combine(tempDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

                    // Path traversal guard
                    if (!destPath.StartsWith(canonicalTemp, StringComparison.OrdinalIgnoreCase))
                        return Fail(pluginId, "Unpack",
                            $"Path traversal detected: archive entry '{entry.FullName}' would escape the temp directory.");

                    if (entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        assetCount++;
                        assetsSize += entry.Length;
                        if (assetCount > pkgOpts.MaxAssetsFileCount)
                            return Fail(pluginId, "Unpack",
                                $"assets/ directory exceeds maximum file count of {pkgOpts.MaxAssetsFileCount}.");
                        if (assetsSize > pkgOpts.MaxAssetsSizeBytes)
                            return Fail(pluginId, "Unpack",
                                $"assets/ directory exceeds maximum size of {pkgOpts.MaxAssetsSizeBytes} bytes.");
                    }

                    var destDir = Path.GetDirectoryName(destPath)!;
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                    await using var entryStream = entry.Open();
                    await using var destStream  = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await entryStream.CopyToAsync(destStream, ct);
                }

                // ── Stage 8: Write derived v1 plugin.json ────────────────────────
                // manifest.Id is validated non-null/non-empty by ManifestV2Validator.Validate above.
                var safeId     = manifest.Id!;
                var v1Manifest = new PluginManifest
                {
                    ManifestVersion = 1,
                    Id              = safeId,
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
                    Permissions     = manifest.Permissions,
                    Dependencies    = manifest.PluginDependencies.Select(d => d.Id).ToList(),
                    Capabilities    = manifest.Capabilities,
                };
                var v1Json = JsonSerializer.Serialize(v1Manifest, V1WriteOpts);
                await File.WriteAllTextAsync(Path.Combine(tempDir, "plugin.json"), v1Json, ct);

                // ── Stage 9: Atomic Move ──────────────────────────────────────────
                var destination = Path.Combine(host.PluginsPath, safeId);
                string? bakDir  = null;

                try
                {
                    if (Directory.Exists(destination))
                    {
                        bakDir = $"{destination}.bak.{DateTime.UtcNow:yyyyMMddHHmmss}";
                        Directory.Move(destination, bakDir);
                    }

                    if (!Directory.Exists(host.PluginsPath))
                        Directory.CreateDirectory(host.PluginsPath);

                    Directory.Move(tempDir, destination);
                    tempDir = string.Empty; // ownership transferred

                    if (bakDir is not null && Directory.Exists(bakDir))
                        Directory.Delete(bakDir, true);
                }
                catch (Exception ex)
                {
                    logger.Log(LogLevel.Warning, RollbackAttempted,
                        "AtomicMove failed for plugin '{PluginId}'. Attempting rollback. Error: {Error}",
                        pluginId, ex.Message);

                    // Attempt to restore .bak
                    if (bakDir is not null && Directory.Exists(bakDir))
                    {
                        try { Directory.Move(bakDir, destination); }
                        catch (Exception rbEx)
                        {
                            logger.LogWarning(rbEx, "Rollback restore failed for '{PluginId}'.", pluginId);
                        }
                    }

                    return Fail(pluginId, "AtomicMove", ex.Message);
                }

                // ── Stage 10: Persist to Store ────────────────────────────────────
                var packageHash = ComputeFileHash(packagePath);
                var record = new PluginRecord
                {
                    PluginId           = safeId,
                    PluginName         = manifest.Name,
                    PluginVersion      = manifest.Version,
                    Status             = PluginStatus.Loaded.ToString(),
                    Enabled            = true,
                    InstalledAt        = DateTime.UtcNow,
                    LastSeenAt         = DateTime.UtcNow,
                    LastError          = null,
                    ManifestHash       = packageHash,
                    HostVersion        = host.HostVersion,
                    PackageHash        = packageHash,
                    SignedBy           = sigResult.IsValid ? sigResult.PublicKeyId : manifest.Signature?.PublicKeyId,
                    SignatureAlgorithm = manifest.Signature?.Algorithm,
                    IsPackageInstall   = true,
                };
                await store.UpsertAsync(record, ct);

                logger.Log(LogLevel.Information, InstallSucceeded,
                    "Plugin '{PluginId}' v{Version} installed successfully.", safeId, manifest.Version);

                return PackageInstallResult.Ok(safeId, manifest.Version);
            }
        }
        catch (Exception ex)
        {
            logger.Log(LogLevel.Warning, InstallFailed,
                "Plugin installation failed at unknown stage. Plugin: '{PluginId}'. Error: {Error}",
                pluginId, ex.Message);
            return Fail(pluginId, "Unknown", ex.Message);
        }
        finally
        {
            // Clean up temp dir if it still exists (failure path)
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to clean up temp directory '{TempDir}'.", tempDir);
                }
            }
        }
    }

    public async Task<bool> UninstallAsync(string pluginId, CancellationToken ct)
    {
        var destination = Path.Combine(hostOptions.Value.PluginsPath, pluginId);
        if (!Directory.Exists(destination)) return false;

        Directory.Delete(destination, true);
        await store.SetEnabledAsync(pluginId, false, ct);

        logger.Log(LogLevel.Information, PluginUninstalled, "Plugin '{PluginId}' uninstalled.", pluginId);
        return true;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private PackageInstallResult Fail(string pluginId, string stage, string error)
    {
        logger.Log(LogLevel.Warning, InstallFailed,
            "Plugin installation failed. PluginId='{PluginId}', Stage='{Stage}', Error='{Error}'",
            pluginId, stage, error);
        return PackageInstallResult.Fail(pluginId, stage, error);
    }

    private static async Task<string> ComputeEntryHashAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        using var incHash  = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer         = new byte[4096];
        await using var stream = entry.Open();
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            incHash.AppendData(buffer, 0, read);
        return Convert.ToHexString(incHash.GetCurrentHash()).ToLowerInvariant();
    }

    private static string ComputeFileHash(string filePath)
    {
        using var incHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer        = new byte[4096];
        using var stream  = File.OpenRead(filePath);
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            incHash.AppendData(buffer, 0, read);
        return Convert.ToHexString(incHash.GetCurrentHash()).ToLowerInvariant();
    }
}
