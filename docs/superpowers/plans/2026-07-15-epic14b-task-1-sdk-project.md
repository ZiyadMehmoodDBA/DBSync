# Epic 14B — Task 1: MSOSync.Sdk Project

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Create the `MSOSync.Sdk` project with all public plugin-author contracts. Zero NuGet dependencies, zero project references. Add it to the solution. Add a project reference from `MSOSync.Plugin` to `MSOSync.Sdk`.

**Architecture:** `MSOSync.Sdk` is a standalone class library containing only interfaces, enums, and the `PluginBase` convenience class. No host code. No framework packages. `Directory.Build.props` provides: `TargetFramework=net9.0`, `LangVersion=13.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`.

**Tech Stack:** C# 13 / .NET 9

## Global Constraints (from master plan)

- `MSOSync.Sdk` must have zero NuGet dependencies and zero project references
- `MSOSync.Plugin` references `MSOSync.Sdk` and `MSOSync.Common` only
- `TreatWarningsAsErrors=true` — no warnings allowed
- Package versions managed centrally in `Directory.Packages.props` — no explicit versions in `.csproj`

## Files

**Create:**
- `src/MSOSync.Sdk/MSOSync.Sdk.csproj`
- `src/MSOSync.Sdk/Abstractions/IPlugin.cs`
- `src/MSOSync.Sdk/Abstractions/IPluginContext.cs`
- `src/MSOSync.Sdk/Abstractions/IPluginConfiguration.cs`
- `src/MSOSync.Sdk/Abstractions/IPluginServices.cs`
- `src/MSOSync.Sdk/Abstractions/IPluginLogger.cs`
- `src/MSOSync.Sdk/Abstractions/IPluginEnvironment.cs`
- `src/MSOSync.Sdk/Metadata/PluginMetadata.cs`
- `src/MSOSync.Sdk/Metadata/PluginCapability.cs`
- `src/MSOSync.Sdk/Metadata/PluginPermission.cs`
- `src/MSOSync.Sdk/Hosting/PluginBase.cs`
- `src/MSOSync.Sdk/Events/.gitkeep` (empty directory marker — Events namespace reserved for 14C)

**Modify:**
- `MSOSync.sln` — add MSOSync.Sdk project entry
- `src/MSOSync.Plugin/MSOSync.Plugin.csproj` — add ProjectReference to MSOSync.Sdk

## Interfaces

**Consumes:** Nothing (this is the root SDK)

**Produces:** (all consumed by Tasks 2, 3, 4, 5, 6, 7, 8, 9)
- `IPlugin` — plugin lifecycle contract
- `IPluginContext` — context passed to `InitializeAsync`
- `IPluginConfiguration` — layered config access
- `IPluginServices` — per-plugin container access
- `IPluginLogger` — structured logging surface
- `IPluginEnvironment` — host environment info
- `PluginMetadata` — immutable plugin metadata
- `PluginCapability` — [Flags] enum
- `PluginPermission` — enum
- `PluginBase` — optional base class caching Context

---

- [ ] **Step 1: Create directory structure**

```powershell
New-Item -ItemType Directory -Path "src\MSOSync.Sdk\Abstractions" -Force
New-Item -ItemType Directory -Path "src\MSOSync.Sdk\Metadata" -Force
New-Item -ItemType Directory -Path "src\MSOSync.Sdk\Hosting" -Force
New-Item -ItemType Directory -Path "src\MSOSync.Sdk\Events" -Force
New-Item -ItemType File -Path "src\MSOSync.Sdk\Events\.gitkeep" -Force
```

- [ ] **Step 2: Create `src/MSOSync.Sdk/MSOSync.Sdk.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- No PackageReference, no ProjectReference: zero dependencies.           -->
  <!-- Directory.Build.props supplies: TargetFramework, LangVersion, Nullable, -->
  <!-- ImplicitUsings, TreatWarningsAsErrors.                                   -->
</Project>
```

- [ ] **Step 3: Create `src/MSOSync.Sdk/Abstractions/IPlugin.cs`**

```csharp
namespace MSOSync.Sdk.Abstractions;

public interface IPlugin : IAsyncDisposable
{
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Create `src/MSOSync.Sdk/Abstractions/IPluginContext.cs`**

```csharp
using MSOSync.Sdk.Metadata;

namespace MSOSync.Sdk.Abstractions;

public interface IPluginContext
{
    PluginMetadata       Metadata      { get; }
    IPluginLogger        Logger        { get; }
    IPluginConfiguration Configuration { get; }
    IPluginServices      Services      { get; }
    IPluginEnvironment   Environment   { get; }
}
```

- [ ] **Step 5: Create `src/MSOSync.Sdk/Abstractions/IPluginConfiguration.cs`**

```csharp
namespace MSOSync.Sdk.Abstractions;

public interface IPluginConfiguration
{
    T?                          GetValue<T>(string key);
    T                           GetValue<T>(string key, T defaultValue);
    IPluginConfiguration        GetSection(string sectionName);
    IReadOnlyCollection<string> Keys  { get; }
    bool                        Exists(string key);
}
```

- [ ] **Step 6: Create `src/MSOSync.Sdk/Abstractions/IPluginServices.cs`**

```csharp
namespace MSOSync.Sdk.Abstractions;

public interface IPluginServices
{
    T              GetRequiredService<T>() where T : notnull;
    T?             GetService<T>();
    IEnumerable<T> GetServices<T>();
}
```

- [ ] **Step 7: Create `src/MSOSync.Sdk/Abstractions/IPluginLogger.cs`**

```csharp
namespace MSOSync.Sdk.Abstractions;

public interface IPluginLogger
{
    void        LogDebug(string message, params object?[] args);
    void        LogInformation(string message, params object?[] args);
    void        LogWarning(string message, params object?[] args);
    void        LogWarning(Exception exception, string message, params object?[] args);
    void        LogError(Exception? exception, string message, params object?[] args);
    void        LogCritical(Exception? exception, string message, params object?[] args);
    IDisposable BeginScope(string name);
}
```

- [ ] **Step 8: Create `src/MSOSync.Sdk/Abstractions/IPluginEnvironment.cs`**

```csharp
namespace MSOSync.Sdk.Abstractions;

public interface IPluginEnvironment
{
    string EnvironmentName { get; }
    bool   IsDevelopment   { get; }
    bool   IsProduction    { get; }
    string HostVersion     { get; }
    string DataDirectory   { get; }
    string PluginDirectory { get; }
}
```

- [ ] **Step 9: Create `src/MSOSync.Sdk/Metadata/PluginCapability.cs`**

```csharp
namespace MSOSync.Sdk.Metadata;

[Flags]
public enum PluginCapability
{
    None      = 0,
    Collector = 1,
    Transport = 2,
    Operation = 4,
    Router    = 8,
    Health    = 16
}
```

- [ ] **Step 10: Create `src/MSOSync.Sdk/Metadata/PluginPermission.cs`**

```csharp
namespace MSOSync.Sdk.Metadata;

public enum PluginPermission
{
    None       = 0,
    Collectors = 1,
    Transport  = 2,
    Operations = 4
}
```

- [ ] **Step 11: Create `src/MSOSync.Sdk/Metadata/PluginMetadata.cs`**

```csharp
namespace MSOSync.Sdk.Metadata;

public sealed record PluginMetadata
{
    public string PluginId     { get; init; } = null!;
    public string Name         { get; init; } = null!;
    public string Version      { get; init; } = null!;
    public string SdkVersion   { get; init; } = null!;
    public string ApiVersion   { get; init; } = null!;
    public string Author       { get; init; } = null!;
    public string Description  { get; init; } = null!;
    public IReadOnlySet<PluginCapability> Capabilities { get; init; } = new HashSet<PluginCapability>();
    public IReadOnlySet<PluginPermission> Permissions  { get; init; } = new HashSet<PluginPermission>();
}
```

- [ ] **Step 12: Create `src/MSOSync.Sdk/Hosting/PluginBase.cs`**

```csharp
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Sdk.Hosting;

public abstract class PluginBase : IPlugin
{
    protected IPluginContext Context { get; private set; } = null!;

    public virtual Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public virtual Task     StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public virtual Task     StopAsync(CancellationToken cancellationToken)  => Task.CompletedTask;
    public virtual ValueTask DisposeAsync()                                 => ValueTask.CompletedTask;
}
```

- [ ] **Step 13: Add `MSOSync.Sdk` to the solution**

Run from the repo root:

```powershell
dotnet sln MSOSync.sln add src\MSOSync.Sdk\MSOSync.Sdk.csproj
```

Expected output: `Project 'src\MSOSync.Sdk\MSOSync.Sdk.csproj' added to the solution.`

- [ ] **Step 14: Add `MSOSync.Sdk` reference to `MSOSync.Plugin`**

Open `src/MSOSync.Plugin/MSOSync.Plugin.csproj`. Add inside the existing `<ItemGroup>` that has `<ProjectReference>` (or create one):

```xml
<ProjectReference Include="..\MSOSync.Sdk\MSOSync.Sdk.csproj" />
```

Final file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 15: Build `MSOSync.Sdk` in isolation to verify zero-dependency rule**

```powershell
dotnet build src\MSOSync.Sdk\MSOSync.Sdk.csproj
```

Expected: `Build succeeded.` with 0 errors, 0 warnings.

- [ ] **Step 16: Build `MSOSync.Plugin` to verify it compiles with the new reference**

```powershell
dotnet build src\MSOSync.Plugin\MSOSync.Plugin.csproj
```

Expected: `Build succeeded.` with 0 errors, 0 warnings. (MSOSync.Plugin compiles without change — it doesn't yet use any Sdk types.)

- [ ] **Step 17: Commit**

```powershell
git add src\MSOSync.Sdk\ src\MSOSync.Plugin\MSOSync.Plugin.csproj MSOSync.sln
git commit -m "feat(14B-1): MSOSync.Sdk project — IPlugin, IPluginContext, IPluginConfiguration, IPluginServices, IPluginLogger, IPluginEnvironment, PluginBase, PluginCapability, PluginPermission, PluginMetadata"
```
