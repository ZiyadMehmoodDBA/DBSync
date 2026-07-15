# Epic 14A — Task 4: PluginRegistry + PluginLoader

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Implement the full `PluginDescriptor` (replacing Task 3 stub), `PluginRuntime` (internal registry storage), `PluginLoadResult`, `IPluginLoader` interface, `PluginRegistry` (thread-safe singleton), and `PluginLoader` (9-stage pipeline). Unit-test registry and loader.

**Architecture:** `PluginRegistry` uses `ConcurrentDictionary<string, PluginRuntime>` internally; `PluginRuntime` wraps the descriptor with optional `AssemblyLoadContext` (null in 14A). `PluginLoader` processes directories alphabetically, runs all 9 stages, registers every processed plugin (Loaded/Failed/Disabled) in the registry. `Skipped` (no plugin.json) entries are NOT registered. `PluginLoader` collects load contexts in its `LoadContexts` property for cleanup at shutdown.

**Tech Stack:** C# 13 / .NET 9 / System.Security.Cryptography / xUnit + FluentAssertions + Moq

## Global Constraints

- `PluginDescriptor` is `public sealed record` — replace the Task 3 stub entirely
- `PluginRuntime` is `internal sealed record` in `MSOSync.Plugin`
- `IPluginLoader.LoadContexts` exposes collected contexts; `PluginHost` (Task 5) disposes them
- `PluginLoader` is scoped/singleton (wired in Task 7); takes `IPluginRegistry`, `IPluginStore`, `IOptions<PluginHostOptions>`, `ILogger<PluginLoader>` — but `PluginHostOptions` is created in Task 5. For this task, inject the `pluginsPath` and `hostVersion` strings directly as a separate parameter or define `PluginHostOptions` now.
- **Define `PluginHostOptions` in this task** to avoid circular dependency with Task 5

## Files

**Create:**
- `src/MSOSync.Plugin/Models/PluginHostOptions.cs`
- `src/MSOSync.Plugin/Models/PluginRuntime.cs`
- `src/MSOSync.Plugin/Models/PluginLoadResult.cs`
- `src/MSOSync.Plugin/Abstractions/IPluginLoader.cs`
- `src/MSOSync.Plugin/Registry/PluginRegistry.cs`
- `src/MSOSync.Plugin/Loading/PluginLoader.cs`
- `tests/MSOSync.PluginTests/Registry/PluginRegistryTests.cs`
- `tests/MSOSync.PluginTests/Loading/PluginLoaderTests.cs`

**Modify:**
- `src/MSOSync.Plugin/Models/PluginDescriptor.cs` — replace stub with full implementation

## Interfaces

**Consumes:**
- `PluginManifest` (Task 2)
- `PluginManifestValidator.Validate(...)` (Task 2)
- `PluginLogEvents` (Task 2)
- `PluginLoadContext` (Task 3)
- `IPluginRegistry` (Task 3)
- `PluginDependencyResolver.Resolve(...)` (Task 3)
- `IPluginStore` (Task 1)
- `PluginStatus`, `PluginLoadOutcome` (Task 1)

**Produces:**
- `PluginDescriptor` — full implementation (consumed by Tasks 5, 6, 7)
- `PluginRegistry` — implementation of `IPluginRegistry` (consumed by Tasks 5, 6, 7)
- `IPluginLoader` interface (consumed by Tasks 5, 7)
- `PluginLoader` — implementation (consumed by Tasks 5, 7)
- `PluginHostOptions` (consumed by Tasks 5, 7)

---

- [ ] **Step 1: Create `src/MSOSync.Plugin/Models/PluginHostOptions.cs`**

```csharp
namespace MSOSync.Plugin.Models;

public sealed class PluginHostOptions
{
    public string PluginsPath  { get; set; } = "plugins";
    public string HostVersion  { get; set; } = "1.0.0";
}
```

- [ ] **Step 2: Replace `src/MSOSync.Plugin/Models/PluginDescriptor.cs` with full implementation**

```csharp
using System.Runtime.Loader;

namespace MSOSync.Plugin.Models;

public sealed record PluginDescriptor
{
    public string       PluginId          { get; init; } = null!;
    public string       Name              { get; init; } = null!;
    public string       Version           { get; init; } = null!;
    public PluginStatus Status            { get; set; }           // mutable for UpdateStatus
    public string?      ErrorMessage      { get; set; }           // mutable for UpdateStatus
    public string?      FailureStage      { get; init; }
    public DateTime     LoadedAt          { get; init; }
    public long         LoadDurationMs    { get; init; }
    public string       HostCompatibility { get; init; } = "Compatible";
    public IReadOnlyList<string> Capabilities  { get; init; } = [];
    public IReadOnlyList<string> Permissions   { get; init; } = [];
    public IReadOnlyList<string> Dependencies  { get; init; } = [];
    public PluginManifest? Manifest        { get; init; }
}
```

- [ ] **Step 3: Create `src/MSOSync.Plugin/Models/PluginRuntime.cs`**

```csharp
using System.Reflection;
using System.Runtime.Loader;

namespace MSOSync.Plugin.Models;

// Internal to MSOSync.Plugin. Never exposed via API.
// Assembly and LoadContext are null in 14A (populated in 14B when plugin activation is added).
internal sealed record PluginRuntime
{
    public PluginDescriptor     Descriptor   { get; set; } = null!;
    public Assembly?            Assembly     { get; init; }
    public AssemblyLoadContext? LoadContext  { get; init; }
}
```

- [ ] **Step 4: Create `src/MSOSync.Plugin/Models/PluginLoadResult.cs`**

```csharp
namespace MSOSync.Plugin.Models;

public sealed record PluginLoadResult(
    string             PluginId,
    PluginLoadOutcome  Outcome,
    string?            FailureStage,
    string?            ErrorMessage);
```

- [ ] **Step 5: Create `src/MSOSync.Plugin/Abstractions/IPluginLoader.cs`**

```csharp
using System.Runtime.Loader;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Abstractions;

public interface IPluginLoader
{
    IReadOnlyList<AssemblyLoadContext> LoadContexts { get; }
    Task<IReadOnlyList<PluginLoadResult>> LoadAllAsync(string pluginsPath, CancellationToken ct);
}
```

- [ ] **Step 6: Create `src/MSOSync.Plugin/Registry/PluginRegistry.cs`**

```csharp
using System.Collections.Concurrent;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Registry;

public sealed class PluginRegistry : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, PluginRuntime> _runtimes =
        new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _initialized;

    public bool IsInitialized => _initialized;

    public IReadOnlyList<PluginDescriptor> GetAll()
        => _runtimes.Values.Select(r => r.Descriptor).ToList();

    public PluginDescriptor? GetById(string pluginId)
        => _runtimes.TryGetValue(pluginId, out var rt) ? rt.Descriptor : null;

    public void Register(PluginDescriptor descriptor)
    {
        var runtime = new PluginRuntime { Descriptor = descriptor };
        _runtimes[descriptor.PluginId] = runtime;
    }

    public void UpdateStatus(string pluginId, PluginStatus status, string? error = null)
    {
        if (_runtimes.TryGetValue(pluginId, out var rt))
        {
            rt.Descriptor.Status       = status;
            rt.Descriptor.ErrorMessage = error;
        }
    }

    public void MarkInitialized() => _initialized = true;
}
```

- [ ] **Step 7: Create `src/MSOSync.Plugin/Loading/PluginLoader.cs`**

```csharp
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Loading;

public sealed class PluginLoader(
    IPluginRegistry           registry,
    IPluginStore              store,
    IOptions<PluginHostOptions> options,
    ILogger<PluginLoader>     logger) : IPluginLoader
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

        // DISCOVER: subdirectories with plugin.json, alphabetical order
        var dirs = Directory.GetDirectories(pluginsPath)
            .Where(d => File.Exists(Path.Combine(d, "plugin.json")))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Pre-load enabled state from store (FILTER stage)
        var storeRecords = (await store.GetAllAsync(ct))
            .ToDictionary(r => r.PluginId, StringComparer.OrdinalIgnoreCase);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            logger.Log(LogLevel.Debug, PluginLogEvents.PluginDirectoryDiscovered,
                "Discovered plugin directory: {Dir}", dir);
            var result = await LoadPluginAsync(dir, storeRecords, seenIds, ct);
            results.Add(result);
        }

        return results;
    }

    private async Task<PluginLoadResult> LoadPluginAsync(
        string dir,
        Dictionary<string, PluginRecord> storeRecords,
        HashSet<string> seenIds,
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
            await PersistAsync(id, manifest?.Name ?? id, manifest?.Version ?? "?",
                PluginStatus.Failed, validationError, manifestHash: null, ct: ct);
            return new PluginLoadResult(id, PluginLoadOutcome.Failed, "ManifestValidation", validationError);
        }

        seenIds.Add(manifest!.Id);

        // Stage 4: FILTER
        if (storeRecords.TryGetValue(manifest.Id, out var rec) && !rec.Enabled)
        {
            RegisterDescriptor(BuildDescriptor(manifest, dir, PluginStatus.Disabled, null, null, TimeSpan.Zero, now));
            logger.Log(LogLevel.Information, PluginLogEvents.PluginDisabled,
                "Plugin {Id} is disabled — skipped", manifest.Id);
            await PersistAsync(manifest.Id, manifest.Name, manifest.Version,
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
            await PersistAsync(manifest.Id, manifest.Name, manifest.Version, PluginStatus.Failed, err, null, ct);
            return new PluginLoadResult(manifest.Id, PluginLoadOutcome.Failed, "HostCompatibility", err);
        }

        // Stage 6: DEPENDENCY RESOLUTION
        var depError = PluginDependencyResolver.Resolve(manifest, registry);
        if (depError != null)
        {
            RegisterFailed(manifest.Id, dir, "DependencyResolution", depError, TimeSpan.Zero, now, manifest);
            await PersistAsync(manifest.Id, manifest.Name, manifest.Version, PluginStatus.Failed, depError, null, ct);
            return new PluginLoadResult(manifest.Id, PluginLoadOutcome.Failed, "DependencyResolution", depError);
        }

        // Stage 7: LOAD
        var sw = System.Diagnostics.Stopwatch.StartNew();
        AssemblyLoadContext? ctx = null;
        System.Reflection.Assembly? assembly;
        try
        {
            var libDir = Path.Combine(dir, "lib");
            ctx      = new PluginLoadContext(dir, Directory.Exists(libDir) ? libDir : null);
            var dll  = Path.Combine(dir, manifest.EntryAssembly);
            assembly = ctx.LoadFromAssemblyPath(dll);
            _loadContexts.Add(ctx);
        }
        catch (Exception ex)
        {
            sw.Stop();
            ctx?.Unload();
            RegisterFailed(manifest.Id, dir, "AssemblyLoad", ex.Message, sw.Elapsed, now, manifest);
            await PersistAsync(manifest.Id, manifest.Name, manifest.Version, PluginStatus.Failed, ex.Message, null, ct);
            return new PluginLoadResult(manifest.Id, PluginLoadOutcome.Failed, "AssemblyLoad", ex.Message);
        }

        // Stage 8: VERIFY ENTRY TYPE
        var entryType = assembly.GetType(manifest.EntryType);
        sw.Stop();
        if (entryType == null)
        {
            var err = $"Type '{manifest.EntryType}' not found in '{manifest.EntryAssembly}'";
            RegisterFailed(manifest.Id, dir, "EntryTypeVerification", err, sw.Elapsed, now, manifest);
            await PersistAsync(manifest.Id, manifest.Name, manifest.Version, PluginStatus.Failed, err, null, ct);
            return new PluginLoadResult(manifest.Id, PluginLoadOutcome.Failed, "EntryTypeVerification", err);
        }

        // Stage 9: METADATA REGISTRATION
        var json2     = await File.ReadAllTextAsync(jsonPath, ct);
        var hash      = ComputeHash(json2);
        var descriptor = BuildDescriptor(manifest, dir, PluginStatus.Loaded, null, null, sw.Elapsed, now);
        RegisterDescriptor(descriptor);

        logger.Log(LogLevel.Information, PluginLogEvents.PluginLoaded,
            "Plugin {Id} v{Version} loaded in {Ms}ms", manifest.Id, manifest.Version, sw.ElapsedMilliseconds);

        await PersistAsync(manifest.Id, manifest.Name, manifest.Version, PluginStatus.Loaded, null, hash, ct);

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
```

- [ ] **Step 8: Create `tests/MSOSync.PluginTests/Registry/PluginRegistryTests.cs`**

```csharp
using FluentAssertions;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using Xunit;

namespace MSOSync.PluginTests.Registry;

public sealed class PluginRegistryTests
{
    private static PluginDescriptor MakeDescriptor(string id, PluginStatus status = PluginStatus.Loaded) => new()
    {
        PluginId = id, Name = id, Version = "1.0.0",
        Status   = status, LoadedAt = DateTime.UtcNow,
    };

    [Fact]
    public void IsInitialized_BeforeMarkInitialized_ReturnsFalse()
    {
        var reg = new PluginRegistry();
        reg.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void MarkInitialized_SetsIsInitializedTrue()
    {
        var reg = new PluginRegistry();
        reg.MarkInitialized();
        reg.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void GetAll_BeforeMarkInitialized_ReturnsEmpty()
    {
        var reg = new PluginRegistry();
        reg.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Register_ThenGetById_ReturnsDescriptor()
    {
        var reg = new PluginRegistry();
        var d   = MakeDescriptor("plugin.a");
        reg.Register(d);
        reg.GetById("plugin.a").Should().NotBeNull();
        reg.GetById("plugin.a")!.PluginId.Should().Be("plugin.a");
    }

    [Fact]
    public void GetById_CaseInsensitive()
    {
        var reg = new PluginRegistry();
        reg.Register(MakeDescriptor("plugin.A"));
        reg.GetById("PLUGIN.A").Should().NotBeNull();
    }

    [Fact]
    public void GetAll_ReturnsAllRegistered()
    {
        var reg = new PluginRegistry();
        reg.Register(MakeDescriptor("plugin.a"));
        reg.Register(MakeDescriptor("plugin.b"));
        reg.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Register_Overwrite_ReplacesExisting()
    {
        var reg = new PluginRegistry();
        reg.Register(MakeDescriptor("plugin.a", PluginStatus.Failed));
        reg.Register(MakeDescriptor("plugin.a", PluginStatus.Loaded));
        reg.GetById("plugin.a")!.Status.Should().Be(PluginStatus.Loaded);
    }

    [Fact]
    public void UpdateStatus_ExistingPlugin_UpdatesStatus()
    {
        var reg = new PluginRegistry();
        reg.Register(MakeDescriptor("plugin.a", PluginStatus.Loaded));
        reg.UpdateStatus("plugin.a", PluginStatus.Failed, "something broke");
        var d = reg.GetById("plugin.a")!;
        d.Status.Should().Be(PluginStatus.Failed);
        d.ErrorMessage.Should().Be("something broke");
    }

    [Fact]
    public void UpdateStatus_UnknownPlugin_DoesNotThrow()
    {
        var reg = new PluginRegistry();
        var act = () => reg.UpdateStatus("no.such.plugin", PluginStatus.Failed);
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 9: Create `tests/MSOSync.PluginTests/Loading/PluginLoaderTests.cs`**

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using Xunit;

namespace MSOSync.PluginTests.Loading;

public sealed class PluginLoaderTests : IDisposable
{
    private readonly string _pluginsRoot = Path.Combine(Path.GetTempPath(), "loader-test-" + Guid.NewGuid().ToString("N"));

    public PluginLoaderTests() => Directory.CreateDirectory(_pluginsRoot);
    public void Dispose() => Directory.Delete(_pluginsRoot, true);

    private PluginLoader MakeLoader(IPluginStore? store = null)
    {
        store ??= Mock.Of<IPluginStore>(s =>
            s.GetAllAsync(It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<PluginRecord>>([]));
        return new PluginLoader(
            new PluginRegistry(),
            store,
            Options.Create(new PluginHostOptions { PluginsPath = _pluginsRoot, HostVersion = "14.0.0" }),
            NullLogger<PluginLoader>.Instance);
    }

    private string CreatePluginDir(string dirName, string pluginId,
        bool createDll = true, string? version = "1.0.0",
        string? minHost = "1.0.0", string? maxHost = "99.9.999",
        string? entryType = "Test.Plugin", bool writeBadJson = false)
    {
        var dir = Path.Combine(_pluginsRoot, dirName);
        Directory.CreateDirectory(dir);

        if (writeBadJson)
        {
            File.WriteAllText(Path.Combine(dir, "plugin.json"), "{ invalid json {{");
            return dir;
        }

        var manifest = $$"""
            {
              "id": "{{pluginId}}",
              "name": "{{pluginId}}",
              "version": "{{version}}",
              "minHostVersion": "{{minHost}}",
              "maxHostVersion": "{{maxHost}}",
              "entryAssembly": "Test.dll",
              "entryType": "{{entryType}}",
              "author": "Test",
              "description": "Test plugin"
            }
            """;
        File.WriteAllText(Path.Combine(dir, "plugin.json"), manifest);

        if (createDll)
        {
            // Copy a real assembly as a stand-in (loader verifies it exists; entryType will be missing)
            var src = typeof(PluginLoaderTests).Assembly.Location;
            File.Copy(src, Path.Combine(dir, "Test.dll"), overwrite: true);
        }

        return dir;
    }

    [Fact]
    public async Task LoadAllAsync_EmptyPluginsDir_ReturnsEmpty()
    {
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAllAsync_MissingPluginsDir_ReturnsEmpty()
    {
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(Path.Combine(_pluginsRoot, "no-such-dir"), default);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAllAsync_BadManifestJson_ReturnsFailed()
    {
        CreatePluginDir("bad-json-plugin", "bad.plugin", writeBadJson: true);
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results.Should().HaveCount(1);
        results[0].Outcome.Should().Be(PluginLoadOutcome.Failed);
        results[0].FailureStage.Should().Be("Parse");
    }

    [Fact]
    public async Task LoadAllAsync_IncompatibleHostVersion_ReturnsFailed()
    {
        CreatePluginDir("compat-plugin", "compat.plugin",
            minHost: "99.0.0", maxHost: "99.9.999");
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results[0].Outcome.Should().Be(PluginLoadOutcome.Failed);
        results[0].FailureStage.Should().Be("HostCompatibility");
    }

    [Fact]
    public async Task LoadAllAsync_DisabledPlugin_ReturnsDisabled()
    {
        CreatePluginDir("disabled-plugin", "disabled.plugin");
        var storeMock = new Mock<IPluginStore>();
        storeMock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PluginRecord>
            {
                new() { PluginId = "disabled.plugin", PluginName = "n", PluginVersion = "1.0.0",
                        Status = "Disabled", Enabled = false, InstalledAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow }
            });
        storeMock.Setup(s => s.UpsertAsync(It.IsAny<PluginRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loader  = MakeLoader(storeMock.Object);
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results[0].Outcome.Should().Be(PluginLoadOutcome.Disabled);
    }

    [Fact]
    public async Task LoadAllAsync_EntryTypeNotFound_ReturnsFailed()
    {
        // Uses the test assembly as "Test.dll" but specifies a non-existent entry type
        CreatePluginDir("bad-type-plugin", "bad.type.plugin",
            entryType: "This.Type.Does.Not.Exist.AtAll");
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results[0].Outcome.Should().Be(PluginLoadOutcome.Failed);
        results[0].FailureStage.Should().Be("EntryTypeVerification");
    }

    [Fact]
    public async Task LoadAllAsync_MissingDll_ReturnsFailed()
    {
        CreatePluginDir("no-dll-plugin", "no.dll.plugin", createDll: false);
        var loader  = MakeLoader();
        var results = await loader.LoadAllAsync(_pluginsRoot, default);
        results[0].Outcome.Should().Be(PluginLoadOutcome.Failed);
        results[0].FailureStage.Should().Be("ManifestValidation");
    }
}
```

- [ ] **Step 10: Run all plugin tests**

```bash
dotnet test tests/MSOSync.PluginTests -v minimal
```

Expected: All tests pass.

- [ ] **Step 11: Commit**

```bash
git add src/MSOSync.Plugin/Models/PluginHostOptions.cs src/MSOSync.Plugin/Models/PluginDescriptor.cs src/MSOSync.Plugin/Models/PluginRuntime.cs src/MSOSync.Plugin/Models/PluginLoadResult.cs src/MSOSync.Plugin/Abstractions/IPluginLoader.cs src/MSOSync.Plugin/Registry/PluginRegistry.cs src/MSOSync.Plugin/Loading/PluginLoader.cs tests/MSOSync.PluginTests/Registry/PluginRegistryTests.cs tests/MSOSync.PluginTests/Loading/PluginLoaderTests.cs
git commit -m "feat(14A-4): PluginDescriptor, PluginRegistry, PluginLoader (9-stage pipeline), unit tests"
```
