using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Packaging;

public static class PluginPacker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Full pack pipeline. Returns 0 on success, 1 on build/IO failure, 2 on manifest validation failure.
    /// </summary>
    public static async Task<int> PackAsync(
        string workingDir,
        string outputDir,
        string configuration,
        string? signingKeyPath,
        CancellationToken ct = default)
    {
        // Step 1+2: Locate and parse manifest
        string manifestPath = Path.Combine(workingDir, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            CliConsole.Error("plugin.json not found in current directory");
            return 2;
        }

        CliPluginManifest? manifest;
        try
        {
            string json = await File.ReadAllTextAsync(manifestPath, ct);
            manifest    = JsonSerializer.Deserialize<CliPluginManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            CliConsole.Error($"Failed to parse plugin.json: {ex.Message}");
            return 2;
        }

        if (manifest is null)
        {
            CliConsole.Error("plugin.json deserialized to null");
            return 2;
        }

        // Step 3: Validate required manifest fields
        if (!ValidateManifest(manifest, out string validationError))
        {
            CliConsole.Error(validationError);
            return 2;
        }

        // Step 4: dotnet publish
        string stageDir = Path.Combine(workingDir, "artifacts", ".msopkg-stage");
        if (Directory.Exists(stageDir))
            Directory.Delete(stageDir, recursive: true);

        int buildResult = await RunDotnetPublishAsync(workingDir, configuration, stageDir, ct);
        if (buildResult != 0)
        {
            CliConsole.Error($"dotnet publish exited with code {buildResult}");
            return 1;
        }

        CliConsole.Ok($"Built: {configuration}");

        // Step 5: Verify entry assembly exists
        string entryAssemblyPath = Path.Combine(stageDir, manifest.EntryAssembly);
        if (!File.Exists(entryAssemblyPath))
        {
            CliConsole.Error($"Entry assembly not found after publish: {manifest.EntryAssembly}");
            return 1;
        }

        // Step 6: Optional signing
        if (!PackageSigningService.TrySign(entryAssemblyPath, signingKeyPath))
            return 1;

        // Step 7+8: Zip to .msopkg and write manifest.json inside archive
        Directory.CreateDirectory(outputDir);
        string pkgFileName = $"{manifest.Id}-{manifest.Version}.msopkg";
        string pkgPath     = Path.Combine(outputDir, pkgFileName);

        if (File.Exists(pkgPath))
            File.Delete(pkgPath);

        // Copy plugin.json into stage dir as manifest.json (canonical archive name)
        File.Copy(manifestPath, Path.Combine(stageDir, "manifest.json"), overwrite: true);

        ZipFile.CreateFromDirectory(stageDir, pkgPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        long sizeKb = new FileInfo(pkgPath).Length / 1024;
        CliConsole.Ok($"Packed: {outputDir}/{pkgFileName} ({sizeKb} KB)");

        // Step 9: Clean stage directory
        Directory.Delete(stageDir, recursive: true);

        return 0;
    }

    /// <summary>Validates that required manifest fields are non-null/non-empty.</summary>
    public static bool ValidateManifest(CliPluginManifest manifest, out string error)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
        { error = "plugin.json: 'id' is required"; return false; }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        { error = "plugin.json: 'name' is required"; return false; }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        { error = "plugin.json: 'version' is required"; return false; }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        { error = "plugin.json: 'entryAssembly' is required"; return false; }

        if (string.IsNullOrWhiteSpace(manifest.EntryType))
        { error = "plugin.json: 'entryType' is required"; return false; }

        error = string.Empty;
        return true;
    }

    private static async Task<int> RunDotnetPublishAsync(
        string workingDir, string configuration, string outputPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList           = { "publish", "-c", configuration, "-o", outputPath },
            WorkingDirectory       = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using Process proc = Process.Start(psi)!;

        // Forward output to console so the user sees build progress
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct);
        return proc.ExitCode;
    }
}
