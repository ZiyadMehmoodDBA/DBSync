# Epic 14B — Task 6: SdkCompatibilityValidator + PluginManifest Extension + PluginActivator

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Extend `PluginManifest` with 14B fields; extend `PluginManifestValidator` to validate them; implement `ISdkCompatibilityValidator`/`SdkCompatibilityValidator` with typed version comparison; implement `PluginActivator` (builds per-plugin sub-container, instantiates `IPlugin`); write `PluginActivatorTests`.

**Architecture:** `PluginManifest` gains `ManifestVersion`, `SdkVersion`, `ApiVersion`, `StartupOrder`. The validator checks them and delegates SDK compatibility to `ISdkCompatibilityValidator`. `PluginActivator.ActivateAsync` runs a 12-step pipeline that builds the per-plugin sub-container, creates `IPluginContext`, and instantiates the plugin. On failure at any step, the plugin's `PluginRuntime.State` is set to `Failed` and the method returns without throwing. `PluginActivator` reads the existing `PluginRuntime` from `PluginRegistry` (the assembly was stored there by the updated `PluginLoader` in Task 8 — but we write a self-contained unit test here using mocks).

**Tech Stack:** C# 13 / .NET 9 / xUnit + FluentAssertions + Moq

## Global Constraints

- `CompatibilityResult` enum values: `Compatible, Warning, Incompatible`
- `sdkVersion` string is parsed to `System.Version` immediately after reading; all comparisons use typed values
- `apiVersion` string is parsed to `int`; mismatch → `Incompatible`
- Host SDK major = `PluginHostOptions.SupportedSdkMajorVersion` (default `"1"`)
- Plugins must have a public parameterless constructor — no constructor injection in 14B
- `PluginActivator.ActivateAsync` NEVER throws — failures set `State = Failed` and log; returns bool (true = success)
- `PluginLoadContext` fix: `AssemblyDependencyResolver` must receive the DLL path, not the directory — fix in `PluginLoadContext` constructor call site in `PluginLoader`

## Files

**Modify:**
- `src/MSOSync.Plugin/Models/PluginManifest.cs` — add ManifestVersion, SdkVersion, ApiVersion, StartupOrder
- `src/MSOSync.Plugin/Loading/PluginManifestValidator.cs` — add validation for new fields
- `src/MSOSync.Plugin/Loading/PluginLoader.cs` — fix AssemblyDependencyResolver to use DLL path; store Assembly+LoadContext in runtime

**Create:**
- `src/MSOSync.Plugin/Lifecycle/CompatibilityResult.cs`
- `src/MSOSync.Plugin/Lifecycle/ISdkCompatibilityValidator.cs`
- `src/MSOSync.Plugin/Lifecycle/SdkCompatibilityValidator.cs`
- `src/MSOSync.Plugin/Lifecycle/PluginActivator.cs`
- `tests/MSOSync.PluginTests/Lifecycle/SdkCompatibilityValidatorTests.cs`
- `tests/MSOSync.PluginTests/Lifecycle/PluginActivatorTests.cs`

## Interfaces

**Consumes:**
- `IPlugin`, `IPluginContext`, `PluginMetadata`, `PluginCapability`, `PluginPermission` (Task 1)
- `PluginLoggerAdapter`, `PluginEnvironmentAdapter`, `PluginServicesAdapter`, `PluginContext` (Task 4)
- `PluginConfigurationFile`, `PluginConfigurationAdapter` (Task 5)
- `PluginRuntime` (existing, will be extended in Task 8 — for now test with a local stub)
- `PluginRegistry.GetRuntime(string)` — internal method added in this task

**Produces:**
- `CompatibilityResult` enum (public — appears in `ISdkCompatibilityValidator`)
- `ISdkCompatibilityValidator.Validate(PluginManifest, out string? message) → CompatibilityResult`
- `SdkCompatibilityValidator` — concrete impl
- `PluginActivator.ActivateAsync(string pluginId, CancellationToken ct) → Task<bool>`
- Extended `PluginManifest` with `ManifestVersion`, `SdkVersion`, `ApiVersion`, `StartupOrder`

---

- [ ] **Step 1: Extend `src/MSOSync.Plugin/Models/PluginManifest.cs`**

Open the file and add four new fields (all optional with defaults):

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Models;

public sealed record PluginManifest
{
    [JsonPropertyName("manifestVersion")] public int    ManifestVersion { get; init; } = 1;
    [JsonPropertyName("id")]              public string Id              { get; init; } = null!;
    [JsonPropertyName("name")]            public string Name            { get; init; } = null!;
    [JsonPropertyName("version")]         public string Version         { get; init; } = null!;
    [JsonPropertyName("sdkVersion")]      public string? SdkVersion     { get; init; }
    [JsonPropertyName("apiVersion")]      public string? ApiVersion     { get; init; }
    [JsonPropertyName("startupOrder")]    public int     StartupOrder   { get; init; } = 1000;
    [JsonPropertyName("minHostVersion")]  public string  MinHostVersion { get; init; } = null!;
    [JsonPropertyName("maxHostVersion")]  public string  MaxHostVersion { get; init; } = null!;
    [JsonPropertyName("entryAssembly")]   public string  EntryAssembly  { get; init; } = null!;
    [JsonPropertyName("entryType")]       public string  EntryType      { get; init; } = null!;
    [JsonPropertyName("author")]          public string  Author         { get; init; } = null!;
    [JsonPropertyName("description")]     public string  Description    { get; init; } = null!;
    [JsonPropertyName("permissions")]     public IReadOnlyList<string> Permissions   { get; init; } = [];
    [JsonPropertyName("dependencies")]    public IReadOnlyList<string> Dependencies  { get; init; } = [];
    [JsonPropertyName("capabilities")]    public IReadOnlyList<string> Capabilities  { get; init; } = [];
}
```

- [ ] **Step 2: Extend `src/MSOSync.Plugin/Loading/PluginManifestValidator.cs`**

After the existing `Dependencies` duplicate check, add validation for the new fields:

```csharp
// After existing no-duplicate-capabilities check, add:

// sdkVersion and apiVersion are required starting with manifestVersion 1
if (string.IsNullOrWhiteSpace(manifest.SdkVersion))
    return "Field 'sdkVersion' is required.";

if (!Version.TryParse(manifest.SdkVersion, out _))
    return $"Field 'sdkVersion' value '{manifest.SdkVersion}' is not a valid version (e.g. '1.0').";

if (string.IsNullOrWhiteSpace(manifest.ApiVersion))
    return "Field 'apiVersion' is required.";

if (!int.TryParse(manifest.ApiVersion, out _))
    return $"Field 'apiVersion' value '{manifest.ApiVersion}' is not a valid integer string.";

if (manifest.StartupOrder < 0)
    return $"Field 'startupOrder' must be non-negative; got {manifest.StartupOrder}.";
```

- [ ] **Step 3: Create `src/MSOSync.Plugin/Lifecycle/CompatibilityResult.cs`**

```csharp
namespace MSOSync.Plugin.Lifecycle;

public enum CompatibilityResult { Compatible, Warning, Incompatible }
```

- [ ] **Step 4: Create `src/MSOSync.Plugin/Lifecycle/ISdkCompatibilityValidator.cs`**

```csharp
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Lifecycle;

internal interface ISdkCompatibilityValidator
{
    CompatibilityResult Validate(PluginManifest manifest, out string? message);
}
```

- [ ] **Step 5: Create `src/MSOSync.Plugin/Lifecycle/SdkCompatibilityValidator.cs`**

```csharp
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Lifecycle;

internal sealed class SdkCompatibilityValidator(IOptions<PluginHostOptions> options) : ISdkCompatibilityValidator
{
    public CompatibilityResult Validate(PluginManifest manifest, out string? message)
    {
        message = null;

        // SdkVersion already validated in PluginManifestValidator — parse is safe here
        if (!Version.TryParse(manifest.SdkVersion, out var pluginSdk))
        {
            message = $"Cannot parse sdkVersion '{manifest.SdkVersion}'";
            return CompatibilityResult.Incompatible;
        }

        if (!int.TryParse(options.Value.SupportedSdkMajorVersion, out var supportedMajor))
            supportedMajor = 1;

        if (pluginSdk.Major != supportedMajor)
        {
            message = $"Plugin sdkVersion major={pluginSdk.Major} is not compatible with host sdkMajor={supportedMajor}";
            return CompatibilityResult.Incompatible;
        }

        // ApiVersion check
        if (!int.TryParse(manifest.ApiVersion, out var pluginApi))
        {
            message = $"Cannot parse apiVersion '{manifest.ApiVersion}'";
            return CompatibilityResult.Incompatible;
        }

        if (!int.TryParse(options.Value.SupportedApiVersion, out var supportedApi))
            supportedApi = 1;

        if (pluginApi != supportedApi)
        {
            message = $"Plugin apiVersion={pluginApi} does not match host apiVersion={supportedApi}";
            return CompatibilityResult.Incompatible;
        }

        return CompatibilityResult.Compatible;
    }
}
```

- [ ] **Step 6: Add `SupportedSdkMajorVersion` and `SupportedApiVersion` to `PluginHostOptions`**

Open `src/MSOSync.Plugin/Models/PluginHostOptions.cs` and add the new fields:

```csharp
namespace MSOSync.Plugin.Models;

public sealed class PluginHostOptions
{
    public string PluginsPath              { get; set; } = "plugins";
    public string HostVersion              { get; set; } = "1.0.0";
    public string SupportedSdkMajorVersion { get; set; } = "1";
    public string SupportedApiVersion      { get; set; } = "1";
}
```

(The full `PluginHostOptions` including timeout and hardening fields is done in Task 8. These two fields are needed here for `SdkCompatibilityValidator`.)

- [ ] **Step 7: Add `GetRuntime` internal method to `src/MSOSync.Plugin/Registry/PluginRegistry.cs`**

Open the file and add after `UpdateStatus`:

```csharp
internal PluginRuntime? GetRuntime(string pluginId)
    => _runtimes.TryGetValue(pluginId, out var rt) ? rt : null;

internal IReadOnlyList<PluginRuntime> GetAllRuntimes()
    => _runtimes.Values.ToList();
```

- [ ] **Step 8: Fix `PluginLoadContext` constructor call in `PluginLoader.cs`**

The existing code passes the plugin *directory* to `PluginLoadContext` which forwards it to `AssemblyDependencyResolver`. `AssemblyDependencyResolver` expects a DLL path, not a directory. Fix the call site:

In `PluginLoader.cs`, find the stage 7 (LOAD) block and update:

```csharp
// OLD:
ctx = new PluginLoadContext(dir, Directory.Exists(libDir) ? libDir : null);

// NEW (pass the DLL path as the component path):
var dllPath = Path.Combine(dir, manifest.EntryAssembly);
ctx         = new PluginLoadContext(dllPath, Directory.Exists(libDir) ? libDir : null);
assembly    = ctx.LoadFromAssemblyPath(dllPath);
```

Also update `PluginLoadContext.cs` to rename the parameter for clarity:

```csharp
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string? _libDirectory;

    public PluginLoadContext(string componentDllPath, string? libDirectory = null)
        : base(isCollectible: true)
    {
        _resolver     = new AssemblyDependencyResolver(componentDllPath);
        _libDirectory = libDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path != null)
            return LoadFromAssemblyPath(path);

        if (_libDirectory != null)
        {
            var libPath = Path.Combine(_libDirectory, $"{assemblyName.Name}.dll");
            if (File.Exists(libPath))
                return LoadFromAssemblyPath(libPath);
        }

        return null;  // fall back to host context
    }
}
```

Also store the Assembly and LoadContext in the runtime after successful load. In the stage 9 block of `PluginLoader.LoadPluginAsync`, after `RegisterDescriptor(descriptor)`, add:

```csharp
// Store assembly and load context in the runtime for the activator
var runtime = registry.GetRuntime(manifest.Id);
if (runtime != null)
{
    runtime.Assembly   = assembly;
    runtime.LoadContext = ctx;
}
```

This requires `PluginRuntime.Assembly` and `PluginRuntime.LoadContext` to be `set` properties. Change them in `PluginRuntime.cs`:

```csharp
// Change from init to set:
public Assembly?            Assembly    { get; set; }
public AssemblyLoadContext? LoadContext { get; set; }
```

- [ ] **Step 9: Create `src/MSOSync.Plugin/Lifecycle/PluginActivator.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Configuration;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Plugin.Runtime;
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Metadata;

namespace MSOSync.Plugin.Lifecycle;

internal sealed class PluginActivator(
    PluginRegistry             registry,
    ILoggerFactory             loggerFactory,
    IHostEnvironment           hostEnvironment,
    IConfiguration             configuration,
    IOptions<PluginHostOptions> options,
    ILogger<PluginActivator>   logger)
{
    public async Task<bool> ActivateAsync(string pluginId, CancellationToken ct)
    {
        var runtime = registry.GetRuntime(pluginId);
        if (runtime is null)
        {
            logger.LogWarning("PluginActivator: no runtime found for {PluginId}", pluginId);
            return false;
        }

        var manifest  = runtime.Descriptor.Manifest;
        var assembly  = runtime.Assembly;

        if (manifest is null || assembly is null)
        {
            SetFailed(runtime, "Activation", new InvalidOperationException("Assembly or manifest is null"));
            return false;
        }

        // Step 1: Resolve type
        var type = assembly.GetType(manifest.EntryType);
        if (type is null)
        {
            SetFailed(runtime, "EntryTypeVerification",
                new InvalidOperationException($"Type '{manifest.EntryType}' not found in assembly"));
            return false;
        }

        // Step 2: Verify type implements IPlugin
        if (!typeof(IPlugin).IsAssignableFrom(type))
        {
            SetFailed(runtime, "SdkCompatibility",
                new InvalidOperationException($"Type '{manifest.EntryType}' does not implement IPlugin"));
            return false;
        }

        // Step 3: Verify public parameterless constructor
        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            SetFailed(runtime, "Activation",
                new InvalidOperationException($"Type '{manifest.EntryType}' must have a public parameterless constructor"));
            return false;
        }

        // Step 4: Build per-plugin sub-container
        var opts             = options.Value;
        var pluginDir        = Path.GetDirectoryName(assembly.Location) ?? opts.PluginsPath;
        var pluginLogger     = new PluginLoggerAdapter(loggerFactory.CreateLogger(pluginId));
        var configSection    = configuration.GetSection($"Plugins:{pluginId}");
        var configFilePath   = Path.Combine(pluginDir, "plugin.config.json");
        var configFile       = PluginConfigurationFile.Load(configFilePath, logger, opts.MaxPluginConfigSizeBytes);
        var pluginConfig     = new PluginConfigurationAdapter(configSection, configFile);
        var pluginEnv        = new PluginEnvironmentAdapter(hostEnvironment, opts, pluginDir);
        var metadata         = BuildMetadata(manifest);

        var services = new ServiceCollection();
        services.AddSingleton<IPluginLogger>(pluginLogger);
        services.AddSingleton<IPluginConfiguration>(pluginConfig);
        services.AddSingleton<IPluginEnvironment>(pluginEnv);
        services.AddSingleton<IPluginServices>(sp => new PluginServicesAdapter(sp));
        var pluginProvider = services.BuildServiceProvider();

        // Step 5: Create context (immutable — never replaced)
        var pluginServices = pluginProvider.GetRequiredService<IPluginServices>();
        var context        = new PluginContext(metadata, pluginLogger, pluginConfig, pluginServices, pluginEnv);

        // Step 6: Instantiate plugin
        IPlugin instance;
        try
        {
            instance = (IPlugin)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            SetFailed(runtime, "Activation", ex);
            return false;
        }

        // Step 7: Store in runtime
        runtime.Instance       = instance;
        runtime.PluginServices = pluginProvider;
        runtime.Context        = context;

        logger.LogInformation("Plugin {PluginId} activated successfully", pluginId);
        return true;

        await Task.CompletedTask; // suppress CS1998 — method is async for future cancellation checks
    }

    private static void SetFailed(PluginRuntime runtime, string stage, Exception ex)
    {
        runtime.Descriptor.Status       = PluginStatus.Failed;
        runtime.Descriptor.ErrorMessage = ex.Message;
    }

    private static PluginMetadata BuildMetadata(PluginManifest manifest)
    {
        var caps = manifest.Capabilities
            .Select(c => Enum.TryParse<PluginCapability>(c, ignoreCase: true, out var v) ? v : (PluginCapability?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToHashSet();

        var perms = manifest.Permissions
            .Select(p => Enum.TryParse<PluginPermission>(p, ignoreCase: true, out var v) ? v : (PluginPermission?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToHashSet();

        return new PluginMetadata
        {
            PluginId     = manifest.Id,
            Name         = manifest.Name,
            Version      = manifest.Version,
            SdkVersion   = manifest.SdkVersion ?? "1.0",
            ApiVersion   = manifest.ApiVersion ?? "1",
            Author       = manifest.Author,
            Description  = manifest.Description,
            Capabilities = caps,
            Permissions  = perms,
        };
    }
}
```

Note: The `await Task.CompletedTask` suppresses CS1998 (async method without await). Remove it later if real async work is added. Alternatively, mark the method as non-async and return `Task.FromResult(true/false)` — use that approach:

```csharp
// Instead of async Task<bool>, use:
public Task<bool> ActivateAsync(string pluginId, CancellationToken ct)
{
    // ... (all the logic above, returning Task.FromResult(true) or Task.FromResult(false))
}
```

Use the non-async pattern since there's no real async work in this method.

Also: `opts.MaxPluginConfigSizeBytes` — this requires that field to exist on `PluginHostOptions`. Since Task 8 adds all the timeout/hardening fields, add `MaxPluginConfigSizeBytes` to the temporary options added in Step 6 above. Make sure the `PluginHostOptions` updated in Step 6 (of this task) includes it:

```csharp
public sealed class PluginHostOptions
{
    public string PluginsPath              { get; set; } = "plugins";
    public string HostVersion              { get; set; } = "1.0.0";
    public string SupportedSdkMajorVersion { get; set; } = "1";
    public string SupportedApiVersion      { get; set; } = "1";
    public long   MaxPluginConfigSizeBytes { get; set; } = 1_048_576;
}
```

- [ ] **Step 10: Write tests `tests/MSOSync.PluginTests/Lifecycle/SdkCompatibilityValidatorTests.cs`**

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Models;
using Xunit;

namespace MSOSync.PluginTests.Lifecycle;

public sealed class SdkCompatibilityValidatorTests
{
    private static SdkCompatibilityValidator Make(string sdkMajor = "1", string apiVersion = "1")
        => new(Options.Create(new PluginHostOptions
        {
            SupportedSdkMajorVersion = sdkMajor,
            SupportedApiVersion      = apiVersion
        }));

    private static PluginManifest Manifest(string sdkVer, string apiVer)
        => new()
        {
            Id = "test", Name = "T", Version = "1.0.0", SdkVersion = sdkVer,
            ApiVersion = apiVer, MinHostVersion = "1.0.0", MaxHostVersion = "99.9.999",
            EntryAssembly = "T.dll", EntryType = "T.T", Author = "A", Description = "D"
        };

    [Fact]
    public void Validate_MatchingSdkAndApi_ReturnsCompatible()
    {
        var result = Make().Validate(Manifest("1.0", "1"), out var msg);
        result.Should().Be(CompatibilityResult.Compatible);
        msg.Should().BeNull();
    }

    [Fact]
    public void Validate_SdkMajorMismatch_ReturnsIncompatible()
    {
        var result = Make(sdkMajor: "1").Validate(Manifest("2.0", "1"), out var msg);
        result.Should().Be(CompatibilityResult.Incompatible);
        msg.Should().Contain("sdkVersion");
    }

    [Fact]
    public void Validate_ApiVersionMismatch_ReturnsIncompatible()
    {
        var result = Make(apiVersion: "1").Validate(Manifest("1.0", "2"), out var msg);
        result.Should().Be(CompatibilityResult.Incompatible);
        msg.Should().Contain("apiVersion");
    }

    [Fact]
    public void Validate_SdkMinorVersionDiffers_StillCompatible()
    {
        // 1.5 has same major (1) as supported major (1)
        var result = Make().Validate(Manifest("1.5", "1"), out _);
        result.Should().Be(CompatibilityResult.Compatible);
    }
}
```

- [ ] **Step 11: Write tests `tests/MSOSync.PluginTests/Lifecycle/PluginActivatorTests.cs`**

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;
using System.Reflection;
using Xunit;

namespace MSOSync.PluginTests.Lifecycle;

public sealed class PluginActivatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly PluginRegistry _registry = new();

    public PluginActivatorTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    // Fake plugin class that implements IPlugin via PluginBase
    public sealed class FakePlugin : PluginBase { }

    // Fake plugin with no parameterless constructor
    public sealed class NoCtorPlugin : PluginBase
    {
        public NoCtorPlugin(string _ ) { }
    }

    // Fake plugin that doesn't implement IPlugin
    public sealed class NotAPlugin { }

    private PluginActivator MakeActivator()
    {
        var hostEnv = new Mock<IHostEnvironment>();
        hostEnv.Setup(e => e.EnvironmentName).Returns("Development");
        hostEnv.Setup(e => e.ContentRootPath).Returns(_tempDir);

        return new PluginActivator(
            _registry,
            NullLoggerFactory.Instance,
            hostEnv.Object,
            new ConfigurationBuilder().Build(),
            Options.Create(new PluginHostOptions()),
            NullLogger<PluginActivator>.Instance);
    }

    private void RegisterPlugin(string pluginId, Type entryType, Assembly assembly)
    {
        var manifest = new PluginManifest
        {
            Id = pluginId, Name = pluginId, Version = "1.0.0",
            SdkVersion = "1.0", ApiVersion = "1", StartupOrder = 1000,
            MinHostVersion = "1.0.0", MaxHostVersion = "99.9.999",
            EntryAssembly = "fake.dll", EntryType = entryType.FullName!,
            Author = "Test", Description = "Test",
        };
        var descriptor = new PluginDescriptor
        {
            PluginId = pluginId, Name = pluginId, Version = "1.0.0",
            Status = PluginStatus.Loaded, LoadedAt = DateTime.UtcNow, Manifest = manifest,
        };
        _registry.Register(descriptor);
        var runtime    = _registry.GetRuntime(pluginId)!;
        runtime.Assembly = assembly;
    }

    [Fact]
    public async Task ActivateAsync_ValidPlugin_ReturnsTrue_SetsInstance()
    {
        RegisterPlugin("test", typeof(FakePlugin), typeof(FakePlugin).Assembly);
        var activator = MakeActivator();

        var result = await activator.ActivateAsync("test", default);

        result.Should().BeTrue();
        _registry.GetRuntime("test")!.Instance.Should().BeOfType<FakePlugin>();
    }

    [Fact]
    public async Task ActivateAsync_ValidPlugin_SetsContext()
    {
        RegisterPlugin("test", typeof(FakePlugin), typeof(FakePlugin).Assembly);
        var activator = MakeActivator();

        await activator.ActivateAsync("test", default);

        _registry.GetRuntime("test")!.Context.Should().NotBeNull();
    }

    [Fact]
    public async Task ActivateAsync_TypeNotInAssembly_ReturnsFalse_SetsDescriptorFailed()
    {
        var manifest = new PluginManifest
        {
            Id = "bad", Name = "bad", Version = "1.0.0",
            SdkVersion = "1.0", ApiVersion = "1", StartupOrder = 1000,
            MinHostVersion = "1.0.0", MaxHostVersion = "99.9.999",
            EntryAssembly = "fake.dll", EntryType = "No.Such.Type",
            Author = "Test", Description = "Test",
        };
        var descriptor = new PluginDescriptor
        {
            PluginId = "bad", Name = "bad", Version = "1.0.0",
            Status = PluginStatus.Loaded, LoadedAt = DateTime.UtcNow, Manifest = manifest,
        };
        _registry.Register(descriptor);
        _registry.GetRuntime("bad")!.Assembly = typeof(FakePlugin).Assembly;

        var result = await MakeActivator().ActivateAsync("bad", default);

        result.Should().BeFalse();
        _registry.GetRuntime("bad")!.Descriptor.Status.Should().Be(PluginStatus.Failed);
    }

    [Fact]
    public async Task ActivateAsync_TypeNotIPlugin_ReturnsFalse()
    {
        RegisterPlugin("notplugin", typeof(NotAPlugin), typeof(NotAPlugin).Assembly);
        var result = await MakeActivator().ActivateAsync("notplugin", default);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ActivateAsync_NoParameterlessConstructor_ReturnsFalse()
    {
        RegisterPlugin("noctor", typeof(NoCtorPlugin), typeof(NoCtorPlugin).Assembly);
        var result = await MakeActivator().ActivateAsync("noctor", default);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ActivateAsync_UnknownPluginId_ReturnsFalse()
    {
        var result = await MakeActivator().ActivateAsync("nonexistent", default);
        result.Should().BeFalse();
    }
}
```

- [ ] **Step 12: Run all new tests**

```powershell
dotnet test tests\MSOSync.PluginTests --filter "SdkCompatibilityValidatorTests|PluginActivatorTests" -v minimal
```

Expected: All tests pass (9 tests).

- [ ] **Step 13: Run all PluginTests to verify no regressions**

```powershell
dotnet test tests\MSOSync.PluginTests -v minimal
```

Expected: All tests pass.

- [ ] **Step 14: Commit**

```powershell
git add src\MSOSync.Plugin\Models\PluginManifest.cs `
        src\MSOSync.Plugin\Models\PluginHostOptions.cs `
        src\MSOSync.Plugin\Models\PluginRuntime.cs `
        src\MSOSync.Plugin\Loading\PluginManifestValidator.cs `
        src\MSOSync.Plugin\Loading\PluginLoader.cs `
        src\MSOSync.Plugin\Loading\PluginLoadContext.cs `
        src\MSOSync.Plugin\Lifecycle\ `
        src\MSOSync.Plugin\Registry\PluginRegistry.cs `
        tests\MSOSync.PluginTests\Lifecycle\
git commit -m "feat(14B-6): ISdkCompatibilityValidator, SdkCompatibilityValidator, PluginActivator; extend PluginManifest; fix AssemblyDependencyResolver path"
```
