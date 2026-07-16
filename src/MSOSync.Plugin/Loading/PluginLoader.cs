using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;

namespace MSOSync.Plugin.Loading;

public sealed class PluginLoader(
    IPluginRegistry              registry,
    IServiceScopeFactory         scopeFactory,
    IOptions<PluginHostOptions>  options,
    ILogger<PluginLoader>        logger) : IPluginLoader
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly List<AssemblyLoadContext> _loadContexts = [];

    public IReadOnlyList<AssemblyLoadContext> LoadContexts => _loadContexts.AsReadOnly();

    public async Task<IReadOnlyList<PluginLoadResult>> LoadAllAsync(
        string pluginsPath, CancellationToken ct)
    {
        var results = new List<PluginLoadResult>();

        if (!Directory.Exists(pluginsPath))
            return results;

        // Stage 1: DISCOVER — subdirectories with plugin.json, alphabetical order
        var dirs = Directory.GetDirectories(pluginsPath)
            .Where(d => File.Exists(Path.Combine(d, "plugin.json")))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Acquire a single scope for the entire startup scan
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPluginStore>();

        // Pre-load enabled state from store (FILTER stage)
        var storeRecords = (await store.GetAllAsync(ct))
            .ToDictionary(r => r.PluginId, StringComparer.OrdinalIgnoreCase);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            logger.Log(LogLevel.Debug, PluginLogEvents.PluginDirectoryDiscovered,
                "Discovered plugin directory: {Dir}", dir);
            var result = await LoadPluginAsync(dir, storeRecords, seenIds, store, ct);
            results.Add(result);
        }

        return results;
    }

    private async Task<PluginLoadResult> LoadPluginAsync(
        string dir,
        Dictionary<string, PluginRecord> storeRecords,
        HashSet<string> seenIds,
        IPluginStore store,
        CancellationToken ct)
    {
        var now      = DateTime.UtcNow;
        var jsonPath = Path.Combine(dir, "plugin.json");

        // Stage 2: PARSE
        PluginManifest? manifest;
        string manifestHash;
        try
        {
            var json     = await File.ReadAllTextAsync(jsonPath, ct);
            manifestHash = ComputeHash(json);
            manifest     = JsonSerializer.Deserialize<PluginManifest>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            var d = RegisterFailed("?", dir, "Parse", ex.Message, TimeSpan.Zero, now, null);
            return new PluginLoadResult(d.PluginId, PluginLoadOutcome.Failed, "Parse", ex.Message);
        }

        // Stage 3: MANIFEST VALIDATION
        var validationError = PluginManifestValidator.Validate(manifest, dir, seenIds);
        if (validationError != null)
        {
            var id = manifest?.Id ?? "?";
            RegisterFailed(id, dir, "ManifestValidation", validationError, TimeSpan.Zero, now, manifest);
            await PersistAsync(store, id, manifest?.Name ?? id, manifest?.Version ?? "?",
                PluginStatus.Failed, validationError, null, ct);
            return new PluginLoadResult(id, PluginLoadOutcome.Failed, "ManifestValidation", validationError);
        }

        seenIds.Add(manifest!.Id);

        // Stage 4: FILTER
        if (storeRecords.TryGetValue(manifest.Id, out var rec) && !rec.Enabled)
        {
            RegisterDescriptor(BuildDescriptor(manifest, dir, PluginStatus.Disabled, null, null, TimeSpan.Zero, now));
            logger.Log(LogLevel.Information, PluginLogEvents.PluginDisabled,
                "Plugin {Id} is disabled — skipped", manifest.Id);
            await PersistAsync(store, manifest.Id, manifest.Name, manifest.Version,
                PluginStatus.Disabled, null, ComputeHash(await File.ReadAllTextAsync(jsonPath, ct)), ct);
            return new PluginLoadResult(manifest.Id, PluginLoadOutcome.Disabled, null, null);
        }

        // Stage 5: HOST COMPATIBILITY
        var hostVer = options.Value.HostVersion;
        if (!Version.TryParse(hostVer, out var hv) ||
            !Version.TryParse(manifest.MinHostVersion, out var minV) ||
            !Version.TryParse(manifest.MaxHostVersion, out var maxV) ||
            hv < minV || hv > maxV)
        {
            var err = $"Host {hostVer} outside plugin range [{manifest.MinHostVersion},{manifest.MaxHostVersion}]";
            RegisterFailed(manifest.Id, dir, "HostCompatibility", err, TimeSpan.Zero, now, manifest, "Incompatible");
            await PersistAsync(store, manifest.Id, manifest.Name, manifest.Version, PluginStatus.Failed, err, null, ct);
            return new PluginLoadResult(manifest.Id, PluginLoadOutcome.Failed, "HostCompatibility", err);
        }

        // Stage 6: DEPENDENCY RESOLUTION
        var depError = PluginDependencyResolver.Resolve(manifest, registry);
        if (depError != null)
        {
            RegisterFailed(manifest.Id, dir, "DependencyResolution", depError, TimeSpan.Zero, now, manifest);
            await PersistAsync(store, manifest.Id, manifest.Name, manifest.Version, PluginStatus.Failed, depError, null, ct);
            return new PluginLoadResult(manifest.Id, PluginLoadOutcome.Failed, "DependencyResolution", depError);
        }

        // Stage 7: LOAD
        var sw = System.Diagnostics.Stopwatch.StartNew();
        AssemblyLoadContext? ctx = null;
        System.Reflection.Assembly? assembly;
        try
        {
            var libDir  = Path.Combine(dir, "lib");
            var dllPath = Path.Combine(dir, manifest.EntryAssembly);
            ctx      = new PluginLoadContext(dllPath, Directory.Exists(libDir) ? libDir : null);
            assembly = ctx.LoadFromAssemblyPath(dllPath);
            _loadContexts.Add(ctx);
        }
        catch (Exception ex)
        {
            sw.Stop();
            ctx?.Unload();
            RegisterFailed(manifest.Id, dir, "AssemblyLoad", ex.Message, sw.Elapsed, now, manifest);
            await PersistAsync(store, manifest.Id, manifest.Name, manifest.Version, PluginStatus.Failed, ex.Message, null, ct);
            return new PluginLoadResult(manifest.Id, PluginLoadOutcome.Failed, "AssemblyLoad", ex.Message);
        }

        // Stage 8: VERIFY ENTRY TYPE
        var entryType = assembly.GetType(manifest.EntryType);
        sw.Stop();
        if (entryType == null)
        {
            var err = $"Type '{manifest.EntryType}' not found in '{manifest.EntryAssembly}'";
            _loadContexts.Remove(ctx!);
            ctx!.Unload();
            RegisterFailed(manifest.Id, dir, "EntryTypeVerification", err, sw.Elapsed, now, manifest);
            await PersistAsync(store, manifest.Id, manifest.Name, manifest.Version, PluginStatus.Failed, err, null, ct);
            return new PluginLoadResult(manifest.Id, PluginLoadOutcome.Failed, "EntryTypeVerification", err);
        }

        // Stage 9: METADATA REGISTRATION
        var json2      = await File.ReadAllTextAsync(jsonPath, ct);
        var hash       = ComputeHash(json2);
        var descriptor = BuildDescriptor(manifest, dir, PluginStatus.Loaded, null, null, sw.Elapsed, now);
        RegisterDescriptor(descriptor);

        // Store assembly and load context in the runtime for the activator
        if (registry is PluginRegistry concreteRegistry)
        {
            var runtime = concreteRegistry.GetRuntime(manifest.Id);
            if (runtime != null)
            {
                runtime.Assembly  = assembly;
                runtime.LoadContext = ctx;
            }
        }

        logger.Log(LogLevel.Information, PluginLogEvents.PluginLoaded,
            "Plugin {Id} v{Version} loaded in {Ms}ms", manifest.Id, manifest.Version, sw.ElapsedMilliseconds);

        await PersistAsync(store, manifest.Id, manifest.Name, manifest.Version, PluginStatus.Loaded, null, hash, ct);

        return new PluginLoadResult(manifest.Id, PluginLoadOutcome.Success, null, null);
    }

    private PluginDescriptor RegisterFailed(
        string id, string dir, string stage, string error, TimeSpan duration,
        DateTime now, PluginManifest? manifest, string hostCompat = "Compatible")
    {
        var descriptor = manifest != null
            ? BuildDescriptor(manifest, dir, PluginStatus.Failed, stage, error, duration, now, hostCompat)
            : new PluginDescriptor
            {
                PluginId = id, Name = id, Version = "?",
                Status = PluginStatus.Failed, FailureStage = stage, ErrorMessage = error,
                LoadedAt = now, HostCompatibility = hostCompat,
            };

        RegisterDescriptor(descriptor);

        logger.Log(LogLevel.Warning, PluginLogEvents.PluginFailed,
            "Plugin {Id} failed at stage {Stage}: {Error}", id, stage, error);

        return descriptor;
    }

    private void RegisterDescriptor(PluginDescriptor descriptor)
        => registry.Register(descriptor);

    private static PluginDescriptor BuildDescriptor(
        PluginManifest manifest, string dir,
        PluginStatus status, string? failureStage, string? errorMessage,
        TimeSpan loadDuration, DateTime now, string hostCompat = "Compatible")
        => new()
        {
            PluginId          = manifest.Id,
            Name              = manifest.Name,
            Version           = manifest.Version,
            Status            = status,
            ErrorMessage      = errorMessage,
            FailureStage      = failureStage,
            LoadedAt          = now,
            LoadDurationMs    = (long)loadDuration.TotalMilliseconds,
            HostCompatibility = hostCompat,
            Capabilities      = manifest.Capabilities,
            Permissions       = manifest.Permissions,
            Dependencies      = manifest.Dependencies,
            Manifest          = manifest,
        };

    private async Task PersistAsync(
        IPluginStore store,
        string pluginId, string name, string version,
        PluginStatus status, string? error, string? hash, CancellationToken ct)
    {
        var rec = new PluginRecord
        {
            PluginId      = pluginId,
            PluginName    = name,
            PluginVersion = version,
            Status        = status.ToString(),
            Enabled       = true,
            InstalledAt   = DateTime.UtcNow,
            LastSeenAt    = DateTime.UtcNow,
            LastError     = error,
            ManifestHash  = hash,
            HostVersion   = options.Value.HostVersion,
        };
        await store.UpsertAsync(rec, ct);
    }

    private static string ComputeHash(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
