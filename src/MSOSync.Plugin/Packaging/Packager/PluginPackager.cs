using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Packaging.Abstractions;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Signing.Abstractions;

namespace MSOSync.Plugin.Packaging.Packager;

public sealed class PluginPackager(
    IOptions<PackagingOptions>  packagingOptions,
    IOptions<PluginHostOptions> hostOptions,
    ILogger<PluginPackager>     logger) : IPluginPackager
{
    // hostOptions is reserved for SDK compatibility checks during install (Task 3)
    private readonly PluginHostOptions _hostOptions = hostOptions.Value;

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    internal static readonly JsonSerializerOptions CanonicalOpts = new()
    {
        WriteIndented          = false,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task PackageAsync(
        string         pluginSourceDirectory,
        string         outputPackagePath,
        IPluginSigner? signingKey,
        CancellationToken ct)
    {
        if (!Directory.Exists(pluginSourceDirectory))
            throw new PluginPackagingException("SourceValidation",
                $"Plugin source directory '{pluginSourceDirectory}' does not exist.");

        // Step 1: Read and parse manifest (accepts plugin.json OR manifest.json)
        var manifestPath = TryFindManifest(pluginSourceDirectory);
        if (manifestPath is null)
            throw new PluginPackagingException("ManifestParse",
                $"No 'plugin.json' or 'manifest.json' found in '{pluginSourceDirectory}'.");

        ManifestV2 manifest;
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, ct);
            manifest = JsonSerializer.Deserialize<ManifestV2>(json, ReadOpts)
                       ?? throw new PluginPackagingException("ManifestParse", "Manifest deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new PluginPackagingException("ManifestParse", ex.Message, ex);
        }

        // Step 2: Validate manifest schema (structural fields only; files[] is computed below)
        var structuralError = ManifestV2Validator.ValidateStructure(manifest);
        if (structuralError is not null)
            throw new PluginPackagingException("ManifestValidation", structuralError);

        // Step 3: Verify entry DLL exists
        var entryDllPath = Path.Combine(pluginSourceDirectory, manifest.EntryAssembly);
        if (!File.Exists(entryDllPath))
            throw new PluginPackagingException("ManifestValidation",
                $"Entry assembly '{manifest.EntryAssembly}' not found in '{pluginSourceDirectory}'.");

        // Step 4: Collect files to hash (DLLs + plugin.config.json if present)
        var filesToHash = CollectHashableFiles(pluginSourceDirectory, manifest);

        // Step 5: Compute SHA-256 of each file
        var fileEntries = new List<PackageFileEntry>();
        foreach (var (relPath, absPath) in filesToHash)
        {
            var hash = await ComputeFileSha256Async(absPath, ct);
            fileEntries.Add(new PackageFileEntry { Path = relPath, Sha256 = hash });
        }

        // Step 6: Inject file hashes into manifest, then run full validation
        manifest = manifest with { Files = fileEntries, Signature = null };
        var validationError = ManifestV2Validator.Validate(manifest);
        if (validationError is not null)
            throw new PluginPackagingException("ManifestValidation", validationError);

        // Step 7: Optionally sign
        if (signingKey is not null)
        {
            var canonicalJson = JsonSerializer.Serialize(manifest, CanonicalOpts);
            var hash          = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
            var sigValue      = signingKey.Sign(hash);
            manifest = manifest with
            {
                Signature = new ManifestSignatureBlock
                {
                    Algorithm   = "RSA-PSS-SHA256",
                    PublicKeyId = signingKey.PublicKeyId,
                    Value       = sigValue,
                },
            };
        }

        // Step 8: Write the .msopkg ZIP archive
        var opts   = packagingOptions.Value;
        var tmpOut = outputPackagePath + ".tmp";

        try
        {
            using (var zipStream = new FileStream(tmpOut, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive   = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                // manifest.json (final, with hashes and optional signature)
                var manifestJson  = JsonSerializer.Serialize(manifest, CanonicalOpts);
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
                await using (var ms = manifestEntry.Open())
                    await ms.WriteAsync(Encoding.UTF8.GetBytes(manifestJson), ct);

                // All listed files
                foreach (var (relPath, absPath) in filesToHash)
                {
                    var entry = archive.CreateEntry(relPath, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var fileStream  = File.OpenRead(absPath);
                    await fileStream.CopyToAsync(entryStream, ct);
                }

                // assets/ (optional, not hash-verified)
                var assetsDir = Path.Combine(pluginSourceDirectory, "assets");
                if (Directory.Exists(assetsDir))
                {
                    int  assetCount = 0;
                    long assetsSize = 0;
                    foreach (var assetFile in Directory.EnumerateFiles(assetsDir, "*", SearchOption.AllDirectories))
                    {
                        assetCount++;
                        var info = new FileInfo(assetFile);
                        assetsSize += info.Length;

                        if (assetCount > opts.MaxAssetsFileCount)
                            throw new PluginPackagingException("ArchiveValidation",
                                $"assets/ directory exceeds maximum file count of {opts.MaxAssetsFileCount}.");
                        if (assetsSize > opts.MaxAssetsSizeBytes)
                            throw new PluginPackagingException("ArchiveValidation",
                                $"assets/ directory exceeds maximum size of {opts.MaxAssetsSizeBytes} bytes.");

                        var relAssetPath = Path.GetRelativePath(pluginSourceDirectory, assetFile)
                                               .Replace('\\', '/');
                        var assetEntry   = archive.CreateEntry(relAssetPath, CompressionLevel.Optimal);
                        await using var aStream = assetEntry.Open();
                        await using var fStream = File.OpenRead(assetFile);
                        await fStream.CopyToAsync(aStream, ct);
                    }
                }
            }

            // Atomic rename
            if (File.Exists(outputPackagePath)) File.Delete(outputPackagePath);
            File.Move(tmpOut, outputPackagePath);

            logger.LogInformation(
                "Plugin packaged: '{Id}' v{Version} => {Output}",
                manifest.Id, manifest.Version, outputPackagePath);
        }
        catch
        {
            if (File.Exists(tmpOut)) File.Delete(tmpOut);
            throw;
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static string? TryFindManifest(string dir)
    {
        // prefer manifest.json (v2), fall back to plugin.json
        var v2 = Path.Combine(dir, "manifest.json");
        if (File.Exists(v2)) return v2;
        var v1 = Path.Combine(dir, "plugin.json");
        if (File.Exists(v1)) return v1;
        return null;
    }

    private static List<(string RelPath, string AbsPath)> CollectHashableFiles(
        string sourceDir, ManifestV2 manifest)
    {
        var result = new List<(string, string)>();

        // Entry DLL (always hashed, at archive root)
        var entryAbs = Path.Combine(sourceDir, manifest.EntryAssembly);
        result.Add((manifest.EntryAssembly, entryAbs));

        // lib/*.dll
        var libDir = Path.Combine(sourceDir, "lib");
        if (Directory.Exists(libDir))
        {
            foreach (var dll in Directory.EnumerateFiles(libDir, "*.dll"))
            {
                var rel = "lib/" + Path.GetFileName(dll);
                result.Add((rel, dll));
            }
        }

        // plugin.config.json (optional)
        var configPath = Path.Combine(sourceDir, "plugin.config.json");
        if (File.Exists(configPath))
            result.Add(("plugin.config.json", configPath));

        return result;
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken ct)
    {
        using var hash   = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var       buffer = new byte[4096];
        await using var fs = File.OpenRead(path);
        int read;
        while ((read = await fs.ReadAsync(buffer, ct)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetCurrentHash()).ToLowerInvariant();
    }
}
