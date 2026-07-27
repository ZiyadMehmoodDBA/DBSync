# API Reference

Complete interface documentation for the MSOSync.Sdk public surface.

## IPlugin

The primary plugin contract. All plugins must implement or extend this.

```csharp
public interface IPlugin : IAsyncDisposable
{
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    ValueTask DisposeAsync(); // from IAsyncDisposable
}
```

**See [Plugin Lifecycle](plugin-lifecycle.md) for detailed contract semantics.**

## IPluginContext

Passed to `InitializeAsync`. Cached by plugins for access to host services.

```csharp
public interface IPluginContext
{
    PluginMetadata       Metadata      { get; }
    IPluginLogger        Logger        { get; }
    IPluginConfiguration Configuration { get; }
    IPluginServices      Services      { get; }
    IPluginEnvironment   Environment   { get; }
}
```

### Properties

| Property | Type | Description | Availability |
|----------|------|---|---|
| `Metadata` | `PluginMetadata` | Plugin ID, name, version | After `InitializeAsync` |
| `Logger` | `IPluginLogger` | Structured logger | After `InitializeAsync` |
| `Configuration` | `IPluginConfiguration` | Config reader | After `InitializeAsync` |
| `Services` | `IPluginServices` | Host services | After `InitializeAsync` |
| `Environment` | `IPluginEnvironment` | Host environment info | After `InitializeAsync` |

Thread-safe for concurrent access. Immutable after `InitializeAsync` returns.

## IPluginLogger

Structured logging interface.

```csharp
public interface IPluginLogger
{
    void LogDebug(string message, params object?[] args);
    void LogInformation(string message, params object?[] args);
    void LogWarning(string message, params object?[] args);
    void LogWarning(Exception exception, string message, params object?[] args);
    void LogError(Exception? exception, string message, params object?[] args);
    void LogCritical(Exception? exception, string message, params object?[] args);
    IDisposable BeginScope(string name);
}
```

### Methods

| Method | Level | When to use |
|--------|-------|---|
| `LogDebug` | Debug | Verbose, temporary diagnostics |
| `LogInformation` | Information | Plugin lifecycle events |
| `LogWarning` | Warning | Recoverable issues |
| `LogError` | Error | Errors (with optional exception) |
| `LogCritical` | Critical | Fatal errors |
| `BeginScope` | N/A | Structured context (using block) |

### Scoped Logging

```csharp
using var scope = Context.Logger.BeginScope("MyMethod");
Context.Logger.LogInformation("message"); // logs with "MyMethod" context
```

Scope is active until disposed.

## IPluginConfiguration

Configuration reader with two-source priority.

```csharp
public interface IPluginConfiguration
{
    T?                          GetValue<T>(string key);
    T                           GetValue<T>(string key, T defaultValue);
    IPluginConfiguration        GetSection(string sectionName);
    IReadOnlyCollection<string> Keys  { get; }
    bool                        Exists(string key);
}
```

**See [Configuration](configuration.md) for usage patterns.**

### Methods

| Method | Returns | Behavior |
|--------|---------|----------|
| `GetValue<T>(key)` | `T?` | Returns null if key not found |
| `GetValue<T>(key, default)` | `T` | Returns default if key not found |
| `GetSection(name)` | `IPluginConfiguration` | Navigate nested sections |
| `Exists(key)` | `bool` | True if key exists (may be null-valued) |
| `Keys` | `IReadOnlyCollection<string>` | All resolved keys at this level |

### Supported Types

`GetValue<T>()` supports: `string`, `int`, `bool`, `double`, `TimeSpan`

Other types throw `InvalidOperationException`.

## IPluginServices

Host service resolver.

```csharp
public interface IPluginServices
{
    T              GetRequiredService<T>() where T : notnull;
    T?             GetService<T>();
    IEnumerable<T> GetServices<T>();
}
```

**See [Services](services.md) for usage patterns.**

### Methods

| Method | Returns | Behavior |
|--------|---------|----------|
| `GetRequiredService<T>()` | `T` | Throws if not registered |
| `GetService<T>()` | `T?` | Returns null if not registered |
| `GetServices<T>()` | `IEnumerable<T>` | Returns all implementations |

### Services Always Available

- `IPluginLogger`
- `IPluginConfiguration`
- `IPluginEnvironment`

### Optional Services

- `IHttpClientFactory` (if host is ASP.NET Core)

## IPluginEnvironment

Host environment information.

```csharp
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

### Properties

| Property | Type | Description | Example |
|----------|------|---|---|
| `EnvironmentName` | `string` | Environment name | `"Production"`, `"Development"`, `"Staging"` |
| `IsDevelopment` | `bool` | True if env is "Development" | `true` or `false` |
| `IsProduction` | `bool` | True if env is "Production" | `true` or `false` |
| `HostVersion` | `string` | Host semantic version | `"14.0.0"` |
| `DataDirectory` | `string` | Absolute path to host data dir | `/var/lib/msosync` |
| `PluginDirectory` | `string` | Absolute path to plugin dir | `/var/lib/msosync/plugins/my-plugin` |

All paths are absolute, normalized, and guaranteed to exist when accessed.

## PluginBase

Convenience base class implementing `IPlugin`.

```csharp
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

**Usage:** Extend this class. Override only the methods you need.

**Context property:** Set during `InitializeAsync`. Available in all derived methods.

## PluginMetadata

Immutable record of plugin information.

```csharp
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

### Properties

| Property | Source | Description |
|----------|--------|---|
| `PluginId` | `plugin.json: id` | Unique plugin identifier |
| `Name` | `plugin.json: name` | Display name |
| `Version` | `plugin.json: version` | Semantic version |
| `SdkVersion` | `plugin.json: sdkVersion` | SDK version used |
| `ApiVersion` | `plugin.json: apiVersion` | API version compatibility |
| `Author` | `plugin.json: author` | Plugin author |
| `Description` | `plugin.json: description` | Long description |
| `Capabilities` | `plugin.json: capabilities` | Declared capabilities |
| `Permissions` | `plugin.json: permissions` | Declared permissions |

Populated from `plugin.json` at activation.

## PluginCapability

Enum describing what the plugin does.

```csharp
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

| Value | Meaning |
|-------|---------|
| `None` | No capability declared (configuration, passive) |
| `Collector` | Reads and collects data from external sources |
| `Transport` | Sends data to external destinations |
| `Operation` | Performs operational mutations (creates, updates, deletes) |
| `Router` | Routes or filters data streams |
| `Health` | Provides health checks |

Declare in `plugin.json: capabilities` array.

## PluginPermission

Enum describing what host resources the plugin accesses.

```csharp
public enum PluginPermission
{
    None       = 0,
    Collectors = 1,
    Transport  = 2,
    Operations = 4
}
```

| Value | Integer | Meaning |
|-------|---------|---------|
| `None` | 0 | No special permissions |
| `Collectors` | 1 | Read from external data sources |
| `Transport` | 2 | Make outbound network calls |
| `Operations` | 4 | Perform mutations |

Declare in `plugin.json: permissions` array as strings: `["Collectors", "Transport"]`.

---

## Version Guarantees

- **SDK 1.0:** All interfaces in this reference are stable
- **SDK 2.0:** New interfaces may be added; existing interfaces are backward compatible
- **Removal:** Interfaces are never removed; deprecated interfaces will be marked with `[Obsolete]`
