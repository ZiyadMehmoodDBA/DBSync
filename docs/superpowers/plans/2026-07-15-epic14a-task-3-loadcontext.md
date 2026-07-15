# Epic 14A — Task 3: PluginLoadContext + IPluginRegistry + PluginDependencyResolver

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Implement `PluginLoadContext` (AssemblyLoadContext subclass), the `IPluginRegistry` interface, and `PluginDependencyResolver` (static class). Unit-test each.

**Architecture:** `PluginLoadContext` resolves assemblies from plugin dir first, then `lib/`, then falls back to default host context. `PluginDependencyResolver.Resolve` takes a manifest and an `IPluginRegistry` reference and verifies all declared dependencies are already `Loaded` in the registry.

**Tech Stack:** C# 13 / .NET 9 / `System.Runtime.Loader` / xUnit + FluentAssertions + Moq

## Global Constraints

- `MSOSync.Plugin` references only `MSOSync.Common`
- `IPluginRegistry.Register(PluginDescriptor)` is fixed (Task 4 implements `PluginDescriptor`)
- `PluginDependencyResolver.Resolve` returns `string? error` (null = OK)
- Alphabetical one-pass resolution: document the 14A limitation in a comment

## Files

**Create:**
- `src/MSOSync.Plugin/Loading/PluginLoadContext.cs`
- `src/MSOSync.Plugin/Abstractions/IPluginRegistry.cs`
- `src/MSOSync.Plugin/Loading/PluginDependencyResolver.cs`
- `tests/MSOSync.PluginTests/Loading/PluginLoadContextTests.cs`
- `tests/MSOSync.PluginTests/Loading/PluginDependencyResolverTests.cs`

## Interfaces

**Consumes:** `PluginStatus` (Task 1), `PluginDescriptor` shape (partially — `IPluginRegistry` references it; Task 4 provides the full type; use a forward reference via interface)

**Note:** `IPluginRegistry` references `PluginDescriptor` which is created in Task 4. To break the circular dependency: define `IPluginRegistry` with a placeholder reference to `PluginDescriptor` in `MSOSync.Plugin.Models` now. Task 4 will implement the actual `PluginDescriptor` class.

**Produces:**
- `PluginLoadContext` (consumed by Task 4 PluginLoader)
- `IPluginRegistry` interface (consumed by Tasks 4, 5, 6, 7)
- `PluginDependencyResolver.Resolve(manifest, registry)` → `string?` (consumed by Task 4)

---

- [ ] **Step 1: Create stub `PluginDescriptor` (will be replaced in Task 4)**

Create `src/MSOSync.Plugin/Models/PluginDescriptor.cs` with a minimal stub so `IPluginRegistry` can compile:

```csharp
namespace MSOSync.Plugin.Models;

// Full implementation in Task 4. Stub allows IPluginRegistry to compile.
public sealed record PluginDescriptor
{
    public string       PluginId         { get; init; } = null!;
    public string       Name             { get; init; } = null!;
    public string       Version          { get; init; } = null!;
    public PluginStatus Status           { get; init; }
    public string?      ErrorMessage     { get; init; }
    public string?      FailureStage     { get; init; }
    public DateTime     LoadedAt         { get; init; }
    public long         LoadDurationMs   { get; init; }
    public string       HostCompatibility { get; init; } = "Compatible";
    public IReadOnlyList<string> Capabilities  { get; init; } = [];
    public IReadOnlyList<string> Permissions   { get; init; } = [];
    public IReadOnlyList<string> Dependencies  { get; init; } = [];
    public PluginManifest? Manifest      { get; init; }
}
```

- [ ] **Step 2: Create `src/MSOSync.Plugin/Abstractions/IPluginRegistry.cs`**

```csharp
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Abstractions;

public interface IPluginRegistry
{
    bool IsInitialized { get; }
    IReadOnlyList<PluginDescriptor> GetAll();
    PluginDescriptor? GetById(string pluginId);
    void Register(PluginDescriptor descriptor);
    void UpdateStatus(string pluginId, PluginStatus status, string? error = null);
    void MarkInitialized();
}
```

- [ ] **Step 3: Create `src/MSOSync.Plugin/Loading/PluginLoadContext.cs`**

```csharp
using System.Reflection;
using System.Runtime.Loader;

namespace MSOSync.Plugin.Loading;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string? _libDirectory;

    public PluginLoadContext(string pluginDirectory, string? libDirectory = null)
        : base(isCollectible: true)
    {
        // Primary resolver targets the plugin's main directory
        _resolver   = new AssemblyDependencyResolver(pluginDirectory);
        _libDirectory = libDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 1. Try plugin main directory
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path != null)
            return LoadFromAssemblyPath(path);

        // 2. Try lib/ subdirectory
        if (_libDirectory != null)
        {
            var libPath = Path.Combine(_libDirectory, $"{assemblyName.Name}.dll");
            if (File.Exists(libPath))
                return LoadFromAssemblyPath(libPath);
        }

        // 3. Fall back to host/shared context
        return null;
    }
}
```

- [ ] **Step 4: Create `src/MSOSync.Plugin/Loading/PluginDependencyResolver.cs`**

```csharp
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Loading;

public static class PluginDependencyResolver
{
    /// <summary>
    /// Verifies all declared dependencies are registered as Loaded in the registry.
    /// Plugins are processed in alphabetical order by directory name (one pass only).
    /// Limitation (14A): dependencies must sort alphabetically before the dependent plugin.
    /// Full dependency graph resolution is a 14B concern.
    /// </summary>
    /// <returns>Null if all dependencies are satisfied, or an error message.</returns>
    public static string? Resolve(PluginManifest manifest, IPluginRegistry registry)
    {
        foreach (var depId in manifest.Dependencies)
        {
            var dep = registry.GetById(depId);
            if (dep == null || dep.Status != PluginStatus.Loaded)
                return $"Dependency '{depId}' is not loaded. Ensure its directory name sorts alphabetically before '{manifest.Id}'.";
        }

        return null;
    }
}
```

- [ ] **Step 5: Write failing tests for `PluginLoadContextTests`**

`tests/MSOSync.PluginTests/Loading/PluginLoadContextTests.cs`:

```csharp
using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using MSOSync.Plugin.Loading;
using Xunit;

namespace MSOSync.PluginTests.Loading;

public sealed class PluginLoadContextTests : IDisposable
{
    // Use this test assembly itself as a known .dll to load from a directory
    private readonly string _dir;
    private readonly string _dllPath;

    public PluginLoadContextTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "plc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        // Copy the test assembly into temp dir so PluginLoadContext can load it
        var src = typeof(PluginLoadContextTests).Assembly.Location;
        _dllPath = Path.Combine(_dir, Path.GetFileName(src));
        File.Copy(src, _dllPath, overwrite: true);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void PluginLoadContext_IsCollectible()
    {
        var ctx = new PluginLoadContext(_dir);
        ctx.IsCollectible.Should().BeTrue();
        ctx.Unload();
    }

    [Fact]
    public void PluginLoadContext_LoadFromAssemblyPath_Succeeds()
    {
        var ctx = new PluginLoadContext(_dir);
        var assembly = ctx.LoadFromAssemblyPath(_dllPath);
        assembly.Should().NotBeNull();
        ctx.Unload();
    }

    [Fact]
    public void PluginLoadContext_LibDirectory_ProbesLib()
    {
        var libDir = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(libDir);

        var src = typeof(PluginLoadContextTests).Assembly.Location;
        var libDll = Path.Combine(libDir, Path.GetFileName(src));
        File.Copy(src, libDll, overwrite: true);

        var ctx = new PluginLoadContext(_dir, libDir);
        // Constructing with libDir should not throw
        ctx.Should().NotBeNull();
        ctx.Unload();
    }

    [Fact]
    public void PluginLoadContext_Unload_DoesNotThrow()
    {
        var ctx = new PluginLoadContext(_dir);
        var act = () => ctx.Unload();
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 6: Write failing tests for `PluginDependencyResolverTests`**

`tests/MSOSync.PluginTests/Loading/PluginDependencyResolverTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using Xunit;

namespace MSOSync.PluginTests.Loading;

public sealed class PluginDependencyResolverTests
{
    private static PluginManifest ManifestWithDeps(params string[] deps) => new()
    {
        Id = "test.plugin", Name = "T", Version = "1.0.0",
        MinHostVersion = "1.0.0", MaxHostVersion = "99.0.0",
        EntryAssembly = "T.dll", EntryType = "T.P",
        Author = "T", Description = "T",
        Dependencies = deps,
    };

    private static IPluginRegistry RegistryWith(string pluginId, PluginStatus status)
    {
        var mock = new Mock<IPluginRegistry>();
        mock.Setup(r => r.GetById(pluginId)).Returns(new PluginDescriptor
        {
            PluginId = pluginId, Name = pluginId, Version = "1.0.0",
            Status   = status,
        });
        return mock.Object;
    }

    [Fact]
    public void Resolve_NoDependencies_ReturnsNull()
    {
        var manifest  = ManifestWithDeps();
        var registry  = new Mock<IPluginRegistry>().Object;
        var result    = PluginDependencyResolver.Resolve(manifest, registry);
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_DependencyLoaded_ReturnsNull()
    {
        var manifest = ManifestWithDeps("dep.plugin");
        var registry = RegistryWith("dep.plugin", PluginStatus.Loaded);
        var result   = PluginDependencyResolver.Resolve(manifest, registry);
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_DependencyNotFound_ReturnsError()
    {
        var manifest = ManifestWithDeps("dep.plugin");
        var mock     = new Mock<IPluginRegistry>();
        mock.Setup(r => r.GetById("dep.plugin")).Returns((PluginDescriptor?)null);
        var result   = PluginDependencyResolver.Resolve(manifest, mock.Object);
        result.Should().NotBeNull().And.Contain("dep.plugin");
    }

    [Fact]
    public void Resolve_DependencyFailed_ReturnsError()
    {
        var manifest = ManifestWithDeps("dep.plugin");
        var registry = RegistryWith("dep.plugin", PluginStatus.Failed);
        var result   = PluginDependencyResolver.Resolve(manifest, registry);
        result.Should().NotBeNull().And.Contain("dep.plugin");
    }
}
```

- [ ] **Step 7: Run all PluginTests**

```bash
dotnet test tests/MSOSync.PluginTests -v minimal
```

Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/MSOSync.Plugin/Models/PluginDescriptor.cs src/MSOSync.Plugin/Abstractions/IPluginRegistry.cs src/MSOSync.Plugin/Loading/PluginLoadContext.cs src/MSOSync.Plugin/Loading/PluginDependencyResolver.cs tests/MSOSync.PluginTests/Loading/PluginLoadContextTests.cs tests/MSOSync.PluginTests/Loading/PluginDependencyResolverTests.cs
git commit -m "feat(14A-3): PluginLoadContext, IPluginRegistry, PluginDependencyResolver with unit tests"
```
