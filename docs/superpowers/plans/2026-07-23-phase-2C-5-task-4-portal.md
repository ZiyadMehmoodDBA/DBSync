# Task 4: Developer Portal (8 Markdown Pages)

**Status:** Ready  
**Estimated time:** 5 hours  
**Dependencies:** Tasks 1–3 (all samples and templates complete)  
**Blocks:** Task 5 (Validation)

---

## Summary

Write eight comprehensive Markdown pages for the developer portal under `docs/developer-portal/`. All pages render correctly on GitHub and in local Markdown viewers. No web server or static site generator needed. All links are relative and internal.

---

## Step 4.1 — Create developer-portal directory

```powershell
$root = "D:\MSOSync"
$portalDir = "$root\docs\developer-portal"

New-Item -ItemType Directory -Force $portalDir | Out-Null
Write-Host "Created $portalDir"
```

- [ ] Developer portal directory created

---

## Page 1: getting-started.md

**File:** `docs/developer-portal/getting-started.md`

```markdown
# Getting Started with MSOSync Plugins

Welcome! This guide will get you from zero to your first running plugin in under 5 minutes.

## Prerequisites

- **.NET 9 SDK** or later installed
- **MSOSync host** installed and running
- **`dotnet new` CLI** available (included with .NET SDK)

## Step 1: Install the Template

Install the MSOSync plugin templates:

\`\`\`bash
dotnet new install MSOSync.Templates
\`\`\`

This registers two templates with the `dotnet` CLI:
- `msosync-plugin` — Basic template for a minimal plugin
- `msosync-plugin-advanced` — Template with configuration and service integration

## Step 2: Scaffold Your First Plugin

Create a new plugin project:

\`\`\`bash
dotnet new msosync-plugin --name MyFirstPlugin
cd MyFirstPlugin
\`\`\`

The template generates these files:

\`\`\`
MyFirstPlugin/
├── MyFirstPlugin.csproj
├── MyFirstPlugin.cs
├── plugin.json
├── plugin.config.json
└── (bin/, obj/ after build)
\`\`\`

### Customize (Optional)

The template accepts additional parameters:

\`\`\`bash
dotnet new msosync-plugin \
  --name AwesomeCollector \
  --pluginId acme.awesome-collector \
  --author "Acme Corp" \
  --description "Collects awesome metrics"
\`\`\`

## Step 3: Build and Verify

\`\`\`bash
dotnet build
\`\`\`

Expected output:

\`\`\`
Build succeeded in 2.345s
\`\`\`

If you see warnings, check your code — the SDK enforces zero warnings.

## Step 4: Drop Into the Host

Deploy the plugin to your MSOSync host. Copy the build output directory to the host's plugins folder:

\`\`\`bash
# Windows PowerShell
Copy-Item -Recurse .\bin\Release\net9.0\* -Destination "{host-path}\plugins\my-first-plugin"

# macOS/Linux bash
cp -r ./bin/Release/net9.0/* {host-path}/plugins/my-first-plugin/
\`\`\`

The host expects this directory layout:

\`\`\`
{host}/plugins/my-first-plugin/
├── MyFirstPlugin.dll
├── plugin.json
├── plugin.config.json
└── (any private dependencies in lib/ subdirectory)
\`\`\`

## Step 5: Restart and Verify

Restart the MSOSync host:

\`\`\`bash
# Assuming host runs as systemd service (Linux)
sudo systemctl restart msosync

# Or if running as a console app, stop and re-run it
\`\`\`

Watch the host logs for success indicators:

\`\`\`
[INFO] PluginHost1002: Plugin my-first-plugin loaded successfully
[INFO] MyFirstPlugin started (host: 14.0.0)
\`\`\`

If you see these lines, your plugin is running!

## Troubleshooting

### Plugin fails to load
- Check `plugin.json` fields are valid (see [Plugin Lifecycle](plugin-lifecycle.md))
- Ensure `entryAssembly` matches your DLL name exactly
- Verify the host can write to the plugin directory (permissions)

### Build fails with warnings as errors
- Check the build output for `warning:` lines
- Common issues:
  - Unused `using` statements
  - Nullable reference warnings (initialize all fields)
  - Unreachable code

## Next Steps

Now that your first plugin runs, explore:

- **[Plugin Lifecycle](plugin-lifecycle.md)** — Understand `InitializeAsync`, `StartAsync`, `StopAsync`, `DisposeAsync`
- **[Configuration](configuration.md)** — Read settings from `plugin.config.json` and host `appsettings.json`
- **[Services](services.md)** — Access host-provided services like `IHttpClientFactory`
- **[Official Samples](../../../samples/)** — See complete implementations of Collector, Transport, and Configuration patterns

## Using the Advanced Template

For more complex plugins, use the advanced template:

\`\`\`bash
dotnet new msosync-plugin-advanced --name MyCollector --capability Collector
\`\`\`

This scaffolds:
- Typed `Settings` record for configuration binding
- `InitializeAsync` for initialization logic
- Timer-based background work in `StartAsync`
- Proper `DisposeAsync` cleanup

See the advanced template's generated comments for guidance.
\`\`\`

---

## Page 2: plugin-lifecycle.md

**File:** `docs/developer-portal/plugin-lifecycle.md`

```markdown
# Plugin Lifecycle Contract

Every MSOSync plugin follows a predictable four-phase lifecycle, from activation to cleanup.

## Lifecycle Diagram

\`\`\`
┌─────────────────────────────────────────────────────────────────┐
│                  Host discovers plugin assembly                 │
└─────────────┬───────────────────────────────────────────────────┘
              │
              ↓
       ┌──────────────┐
       │   Loaded     │  Assembly is in memory; manifest validated
       └──────┬───────┘
              │
              ↓
       ┌──────────────────┐
       │   Initializing   │  All plugins' InitializeAsync called
       │                  │  (in startup order)
       └──────┬───────────┘
              │
              ↓
       ┌──────────────────┐
       │  Initialized     │  All plugins are ready
       └──────┬───────────┘
              │
              ↓
       ┌──────────────────┐
       │    Starting      │  All plugins' StartAsync called
       │                  │  (in startup order)
       └──────┬───────────┘
              │
              ↓
       ┌──────────────────┐
       │    Running       │  Plugin is operational
       └──────┬───────────┘
              │ (host shutdown)
              ↓
       ┌──────────────────┐
       │    Stopping      │  StopAsync called
       │                  │  (in reverse startup order)
       └──────┬───────────┘
              │
              ↓
       ┌──────────────────┐
       │    Stopped       │  Plugin has shut down gracefully
       └──────┬───────────┘
              │
              ↓
       ┌──────────────────┐
       │   Disposing      │  DisposeAsync called
       │                  │  (cleanup resources)
       └──────┬───────────┘
              │
              ↓
       ┌──────────────────┐
       │    Disposed      │  All resources cleaned up
       └──────────────────┘
\`\`\`

## Phase 1: InitializeAsync

**When:** After plugin assembly loads, before other plugins start

**Contract:**
\`\`\`csharp
public virtual Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
{
    Context = context; // Cache the context for later use
    return Task.CompletedTask;
}
\`\`\`

**At this point:**
- `IPluginContext` is available (provides Logger, Configuration, Services, Environment, Metadata)
- The plugin is responsible for acquiring expensive resources (DB connections, HTTP clients, file handles)
- Configuration is readable via `Context.Configuration`
- Other plugins may not have initialized yet

**Best practices:**
- Validate configuration at this stage; throw if critical config is missing
- Do not wait for other plugins to complete
- Do not perform I/O that might block indefinitely
- If you throw `OperationCanceledException`, the host marks this plugin as `Failed`

**Typical code pattern:**
\`\`\`csharp
public override Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
{
    Context = context;
    
    // Validate critical config
    var dbConnStr = Context.Configuration.GetValue<string>("Database", "");
    if (string.IsNullOrEmpty(dbConnStr))
    {
        throw new InvalidOperationException("Database connection string is required");
    }
    
    return Task.CompletedTask;
}
\`\`\`

## Phase 2: StartAsync

**When:** After all plugins complete `InitializeAsync` (in startup order)

**Contract:**
\`\`\`csharp
public virtual Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
\`\`\`

**At this point:**
- All plugins are initialized
- Your `Context` is cached and ready
- Time to start background threads, timers, listeners

**Important:** Do not block `StartAsync`. Return immediately. Use `System.Threading.Timer` or background `Task.Run()` for ongoing work.

**Anti-pattern:**
\`\`\`csharp
public override Task StartAsync(CancellationToken cancellationToken)
{
    while (true) // ❌ WRONG: Blocks the host startup
    {
        DoWork();
    }
}
\`\`\`

**Correct pattern:**
\`\`\`csharp
private Timer _workTimer;

public override Task StartAsync(CancellationToken cancellationToken)
{
    _workTimer = new Timer(
        _ => DoWork(),
        null,
        TimeSpan.Zero,
        TimeSpan.FromSeconds(30));
    
    return Task.CompletedTask;
}
\`\`\`

## Phase 3: StopAsync

**When:** Host is shutting down (triggered by `Ctrl+C`, service stop, etc.)

**Contract:**
\`\`\`csharp
public virtual Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
\`\`\`

**At this point:**
- Host is shutting down
- Plugins are called in **reverse** startup order
- You have a limited time to shut down cleanly (governed by `PluginHostOptions.StopTimeoutSeconds`)
- The `cancellationToken` will be signaled if timeout is reached

**Best practices:**
- Stop background work: dispose timers, cancel in-flight requests
- Do not throw; log errors instead
- Respect the cancellation token
- Unsubscribe from events

**Typical code pattern:**
\`\`\`csharp
public override Task StopAsync(CancellationToken cancellationToken)
{
    Context.Logger.LogInformation("Stopping plugin");
    
    _workTimer?.Dispose();
    cancellationToken.ThrowIfCancellationRequested();
    
    return Task.CompletedTask;
}
\`\`\`

## Phase 4: DisposeAsync

**When:** After `StopAsync`, always (regardless of plugin state)

**Contract:**
\`\`\`csharp
public virtual ValueTask DisposeAsync()
{
    return ValueTask.CompletedTask;
}
\`\`\`

**At this point:**
- Plugin is no longer running
- Final chance to clean up resources
- Called regardless of whether `InitializeAsync` or `StartAsync` succeeded or failed
- Do not throw

**Best practices:**
- Dispose `IDisposable` objects
- Dispose `HttpClient` if you created it
- Close database connections
- Unsubscribe from remaining events

**Typical code pattern:**
\`\`\`csharp
public override async ValueTask DisposeAsync()
{
    _workTimer?.Dispose();
    _httpClient?.Dispose();
    await base.DisposeAsync();
}
\`\`\`

## PluginBase Convenience

The `PluginBase` abstract class provides default implementations:

\`\`\`csharp
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
\`\`\`

**You only override the methods you need.** If your plugin has no cleanup, you don't need `DisposeAsync`. If it has no initialization, `InitializeAsync` is already no-op.

## Failure Behavior

### If InitializeAsync throws

1. Plugin state becomes `Failed`
2. `StartAsync` is never called
3. Other plugins continue normally
4. `StopAsync` is called anyway
5. `DisposeAsync` is called anyway

### If StartAsync throws

1. Plugin state becomes `Failed`
2. `StopAsync` is called anyway
3. `DisposeAsync` is called anyway

### If StopAsync throws

1. Logged as warning
2. `DisposeAsync` is called anyway

### If DisposeAsync throws

1. Logged as critical error
2. Host continues

## Timeouts

Each phase has a timeout (default: 30 seconds). If a method doesn't complete in time:

1. Host cancels via the `CancellationToken`
2. If the method respects `cancellationToken`, it throws `OperationCanceledException`
3. Plugin transitions to `Failed` state

**Always respect the `cancellationToken`** in `InitializeAsync` and `StopAsync`.

## Cancellation Token Semantics

The `CancellationToken` passed to `InitializeAsync` and `StopAsync` signals:
- Timeout reached (per-phase timeout)
- Host shutdown (in `StopAsync` only)

If you receive a signaled token:
1. Stop I/O operations immediately
2. Clean up partial state
3. Throw `OperationCanceledException` or return gracefully (do not throw in `DisposeAsync`)

## Key Rules

| Rule | Reason |
|------|--------|
| Do not block in `StartAsync` | Host startup hangs |
| Do not mutate `IPluginContext` after `InitializeAsync` | Context is cached by plugins and host |
| Do not call `Environment.Exit` from a plugin | Halts entire host |
| Always dispose timers in `DisposeAsync` | Prevents thread leaks |
| Respect `CancellationToken` in `InitializeAsync` and `StopAsync` | Allow graceful timeout handling |
| Never throw from `DisposeAsync` | Host cannot recover |

\`\`\`

---

## Page 3: configuration.md

**File:** `docs/developer-portal/configuration.md`

```markdown
# Configuration Guide

Plugins read configuration from two sources with clear priority semantics.

## Two-Source Configuration Model

**Priority (high to low):**

1. **Host `appsettings.json`** — Section: `Plugins:{pluginId}:*`
2. **Plugin `plugin.config.json`** — In the plugin directory

When you call `Context.Configuration.GetValue("Key")`, the host resolves in priority order. First source that has the key wins.

### Example

Plugin ID: `acme.my-plugin`

**plugin.config.json** (low priority):
\`\`\`json
{
  "DatabaseUrl": "localhost:5432",
  "Timeout": 30
}
\`\`\`

**Host appsettings.json** (high priority):
\`\`\`json
{
  "Plugins": {
    "acme.my-plugin": {
      "DatabaseUrl": "prod-db.acme.com:5432"
    }
  }
}
\`\`\`

**Resolution:**
- `GetValue("DatabaseUrl")` returns `"prod-db.acme.com:5432"` (from appsettings.json)
- `GetValue("Timeout")` returns `30` (from plugin.config.json)

This allows deployments to override sensitive values (database URLs, API keys) without modifying the plugin package.

## Reading Scalar Values

\`\`\`csharp
// With default
var timeout = Context.Configuration.GetValue("Timeout", 30);

// Without default (returns null if not found)
var? url = Context.Configuration.GetValue<string>("DatabaseUrl");
if (url != null)
{
    // use it
}
\`\`\`

**Supported types:** `string`, `int`, `bool`, `double`, `TimeSpan`

### TimeSpan Parsing

`TimeSpan` values use ISO 8601 format:

\`\`\`json
{
  "PollingInterval": "00:00:30"
}
\`\`\`

\`\`\`csharp
var interval = Context.Configuration.GetValue<TimeSpan>("PollingInterval");
// Result: TimeSpan(30 seconds)
\`\`\`

## Reading Sections (Nested Config)

\`\`\`csharp
var retryConfig = Context.Configuration.GetSection("Retry");
var maxAttempts = retryConfig.GetValue("MaxAttempts", 3);
var delayMs = retryConfig.GetValue("DelayMs", 1000);
\`\`\`

Nesting is arbitrary depth:

\`\`\`csharp
Context.Configuration
    .GetSection("Database")
    .GetSection("Replica")
    .GetValue<string>("ConnectionString");
\`\`\`

## Checking Existence

\`\`\`csharp
if (Context.Configuration.Exists("OptionalFeature"))
{
    EnableOptionalFeature();
}
\`\`\`

## Enumerating Keys

\`\`\`csharp
var allKeys = Context.Configuration.Keys;
Context.Logger.LogInformation("Configured keys: {Count}", allKeys.Count);

foreach (var key in allKeys)
{
    Context.Logger.LogDebug("  - {Key}", key);
}
\`\`\`

**Note:** Key order is undefined. Do not depend on order.

## Typed Configuration Binding Pattern

There is no `Bind<T>()` method in SDK 1.0. Use manual binding:

\`\`\`csharp
internal sealed record AppSettings(
    string DatabaseUrl,
    int MaxConnections,
    bool EnableCache);

private AppSettings LoadSettings()
{
    var dbSection = Context.Configuration.GetSection("Database");
    var cacheSection = Context.Configuration.GetSection("Cache");
    
    return new AppSettings(
        DatabaseUrl: dbSection.GetValue("Url", ""),
        MaxConnections: dbSection.GetValue("MaxConnections", 10),
        EnableCache: cacheSection.GetValue("Enabled", false));
}
\`\`\`

Call this in `InitializeAsync` to load settings once, or in a timer to implement hot-reload (see below).

## Hot-Reload Workaround

SDK 1.0 does not provide change notifications. To detect config changes at runtime:

\`\`\`csharp
private Timer _configCheckTimer;
private AppSettings _cachedSettings;

public override Task StartAsync(CancellationToken cancellationToken)
{
    _cachedSettings = LoadSettings();
    
    _configCheckTimer = new Timer(
        _ => CheckForChanges(),
        null,
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(30));
    
    return Task.CompletedTask;
}

private void CheckForChanges()
{
    var newSettings = LoadSettings();
    
    if (newSettings.DatabaseUrl != _cachedSettings.DatabaseUrl)
    {
        Context.Logger.LogInformation(
            "Config changed: DatabaseUrl {Old} → {New}",
            _cachedSettings.DatabaseUrl,
            newSettings.DatabaseUrl);
        _cachedSettings = newSettings;
    }
    
    // Check other fields similarly
}

public override async ValueTask DisposeAsync()
{
    _configCheckTimer?.Dispose();
    await base.DisposeAsync();
}
\`\`\`

This polls every 30 seconds and logs changes. In production, consider longer intervals to reduce overhead.

**Future:** SDK 2.0 will add `IPluginConfigurationMonitor<T>` with reactive change notifications.

## plugin.config.json Format

**Location:** Same directory as the plugin DLL

**Format:** JSON flat or nested objects (no arrays at root)

**Size limit:** 1 MB

**On parse failure:** Host logs warning and ignores the file; configuration comes from appsettings.json only.

### Example: Flat

\`\`\`json
{
  "Enabled": true,
  "TimeoutSeconds": 30,
  "RetryCount": 3
}
\`\`\`

### Example: Nested

\`\`\`json
{
  "Database": {
    "Url": "localhost",
    "Port": 5432
  },
  "Retry": {
    "MaxAttempts": 3,
    "DelayMs": 1000
  }
}
\`\`\`

## Configuration Override Precedence (Complete)

\`\`\`
1. appsettings.json (environment-specific: appsettings.Production.json, etc.)
   ↓ Plugins:{pluginId}:{key}
2. appsettings.json (base)
   ↓ Plugins:{pluginId}:{key}
3. plugin.config.json
   ↓ {key}
4. GetValue<T>(key, defaultValue)
   ↓ defaultValue
5. GetValue<T>(key)
   ↓ null (T?)
\`\`\`

## Best Practices

- **Validate in `InitializeAsync`:** Throw if critical config is missing
- **Log config values in `InitializeAsync` (dev only):** `if (Context.Environment.IsDevelopment)`
- **Use `GetValue(key, default)` for optional keys:** Always provide a sensible default
- **Use `GetValue<T>(key)` only if key is required:** Document why the key must exist
- **Avoid reading config in hot loops:** Cache at init, poll periodically for changes
- **Use `TimeSpan` type for durations:** Avoids hardcoding seconds/milliseconds

\`\`\`

---

## Page 4: services.md

**File:** `docs/developer-portal/services.md`

```markdown
# Accessing Host Services

Plugins can request services from the host via `IPluginServices`.

## What is IPluginServices?

A restricted view of the host's dependency injection (DI) container, exposing only services the host explicitly allows plugins to use.

\`\`\`csharp
public interface IPluginServices
{
    T              GetRequiredService<T>() where T : notnull;
    T?             GetService<T>();
    IEnumerable<T> GetServices<T>();
}
\`\`\`

**You access it via:**
\`\`\`csharp
Context.Services.GetRequiredService<IHttpClientFactory>()
\`\`\`

## GetRequiredService<T>() vs GetService<T>()

| Method | Returns | Behavior | Use case |
|--------|---------|----------|----------|
| `GetRequiredService<T>()` | `T` | Throws `InvalidOperationException` if not registered | Service is **required** for plugin function |
| `GetService<T>()` | `T?` | Returns `null` if not registered | Service is **optional**; fallback available |

### Example: Required Service

\`\`\`csharp
var logger = Context.Services.GetRequiredService<IPluginLogger>();
logger.LogInformation("required service works");
\`\`\`

If the host hasn't registered `IPluginLogger`, this throws. But `IPluginLogger` is always registered, so this never fails.

### Example: Optional Service

\`\`\`csharp
var factory = Context.Services.GetService<IHttpClientFactory>();
if (factory != null)
{
    var client = factory.CreateClient();
    // use client
}
else
{
    // fallback: create own HttpClient
    var client = new HttpClient();
}
\`\`\`

If the host hasn't registered `IHttpClientFactory`, `GetService<T>()` returns `null`. Your plugin gracefully falls back.

## GetServices<T>()

Returns all registered implementations of a service interface:

\`\`\`csharp
var loggers = Context.Services.GetServices<IPluginLogger>();
foreach (var logger in loggers)
{
    logger.LogInformation("message");
}
\`\`\`

Use when multiple plugins might register the same interface.

## Services Available in SDK 1.0

These services are **always** registered:

| Service | Via Context | Via Services | Notes |
|---------|---|---|---|
| `IPluginLogger` | `Context.Logger` | `GetRequiredService<IPluginLogger>()` | Always available |
| `IPluginConfiguration` | `Context.Configuration` | `GetRequiredService<IPluginConfiguration>()` | Always available |
| `IPluginEnvironment` | `Context.Environment` | `GetRequiredService<IPluginEnvironment>()` | Always available |

**Optional services** (use `GetService<T>()`):

| Service | When registered | Use case |
|---------|---|---|
| `IHttpClientFactory` | If host is ASP.NET Core | Create pooled HTTP clients |

### Getting IHttpClientFactory

\`\`\`csharp
public override Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
{
    Context = context;
    
    var factory = Context.Services.GetService<IHttpClientFactory>();
    if (factory != null)
    {
        _httpClient = factory.CreateClient();
        _ownsHttpClient = false;
    }
    else
    {
        _httpClient = new HttpClient();
        _ownsHttpClient = true;
    }
    
    return Task.CompletedTask;
}

public override async ValueTask DisposeAsync()
{
    if (_ownsHttpClient)
    {
        _httpClient?.Dispose();
    }
    await base.DisposeAsync();
}
\`\`\`

## Extension Points (Future)

Future phases (14C and beyond) will add interfaces for plugin-to-host communication:

- `IPluginDataCollector` — Register a collector plugin's output service
- `IPluginTransport` — Register a transport plugin's delivery service

When those interfaces are added, they'll be registered as host services and accessible via:

\`\`\`csharp
var collectors = Context.Services.GetServices<IPluginDataCollector>();
\`\`\`

For now (SDK 1.0), only the four context services and `IHttpClientFactory` are available.

## Thread Safety

Services are resolved at plugin activation time. It's safe to call `GetService<T>()` / `GetRequiredService<T>()` from any plugin method (and from background threads).

## Anti-Patterns

### ❌ Don't cast IPluginServices to IServiceProvider

\`\`\`csharp
// WRONG
var provider = (IServiceProvider)Context.Services;
var anything = provider.GetService(typeof(object));
\`\`\`

`IPluginServices` is not castable to the full `IServiceProvider`. It's intentionally restricted.

### ❌ Don't store IPluginServices after DisposeAsync

\`\`\`csharp
private IPluginServices _services; // WRONG: store after DisposeAsync

public override Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
{
    _services = context.Services; // Don't do this
}
\`\`\`

Resolve services when you need them, not as a cached reference.

### ❌ Don't access database contexts via services

Plugins should not directly access the host's database context. Use documented extension point interfaces instead (when available).

\`\`\`

---

## Page 5: permissions.md

**File:** `docs/developer-portal/permissions.md`

```markdown
# Plugin Permissions Model

Permissions declare what host resources a plugin may need. In SDK 1.0, they are **informational**. In future phases, enforcement will arrive.

## What Are Permissions?

Declared intent in `plugin.json`:

\`\`\`json
{
  "permissions": ["Collectors", "Transport"]
}
\`\`\`

These strings correspond to `PluginPermission` enum values:

| Value | Meaning |
|-------|---------|
| `None` | No special access required (default) |
| `Collectors` | Plugin reads data from external sources |
| `Transport` | Plugin makes outbound network calls |
| `Operations` | Plugin performs mutation/operational actions |

## Capabilities vs Permissions

**Capability:** What the plugin does
- `Collector`, `Transport`, `Operation`, `Router`, `Health`

**Permission:** What host resources it needs
- `Collectors`, `Transport`, `Operations`

**Relationship:** A plugin declaring capability `Collector` typically also declares permission `Collectors`.

### Example Declarations

**Data Collector (reads from DB):**
\`\`\`json
{
  "capabilities": ["Collector"],
  "permissions": ["Collectors"]
}
\`\`\`

**Webhook Plugin (posts to external URL):**
\`\`\`json
{
  "capabilities": ["Transport"],
  "permissions": ["Transport"]
}
\`\`\`

**Multi-permission Plugin (polls + posts):**
\`\`\`json
{
  "capabilities": ["Collector", "Transport"],
  "permissions": ["Collectors", "Transport"]
}
\`\`\`

**Passive Config Reader (no special access):**
\`\`\`json
{
  "capabilities": [],
  "permissions": []
}
\`\`\`

## Declaring Permissions in plugin.json

\`\`\`json
{
  "manifestVersion": 1,
  "id": "acme.my-plugin",
  "name": "My Plugin",
  ...
  "permissions": ["Collectors", "Transport"],
  "capabilities": ["Collector", "Transport"]
}
\`\`\`

Unknown permission strings are logged as warnings and ignored.

## Enforcement (Future)

**Current state (SDK 1.0):**
- Permissions are read and logged
- No runtime enforcement
- Plugin can function regardless

**Future (1.1+):**
- Admin must explicitly grant declared permissions in host configuration
- Plugin fails to load if permissions not granted
- Audit trail of permission grants

**Why declare them now?**
- Document your plugin's access model
- Prepare for future enforcement
- Enable permission-based filtering in the marketplace

## Combined Permissions

A plugin may declare multiple permissions:

\`\`\`json
{
  "permissions": ["Collectors", "Transport", "Operations"]
}
\`\`\`

This is valid and common for complex plugins.

## Best Practices

- **Declare only the permissions you actually use** — more permissions = higher user friction in future phases
- **Match capabilities to permissions** — if you declare `Collector` capability, also declare `Collectors` permission
- **Document why** — add README section explaining what external access the plugin requires
- **Test with future enforcement in mind** — assume permissions will be denied and test graceful fallback

## Permission Reference

| Permission | Integer | Meaning | Example Use Case |
|-----------|---------|---------|---|
| `None` | 0 | No special access required | Configuration validator plugin |
| `Collectors` | 1 | Read from external data sources | SQL database poller, file reader |
| `Transport` | 2 | Make outbound network calls | Webhook sender, syslog forwarder |
| `Operations` | 4 | Perform mutations | Sync trigger, event publisher |

\`\`\`

---

## Page 6: packaging.md

**File:** `docs/developer-portal/packaging.md`

```markdown
# Creating a Plugin Package (.msopkg)

Packages are required for marketplace submission. Optional for local deployment.

## What is a .msopkg?

A signed ZIP archive containing your plugin DLL, manifest, configuration, and optional private dependencies.

## .msopkg Internal Layout

\`\`\`
acme-my-plugin-1.0.0.msopkg
├── plugin.json                    ← manifest (must be at root)
├── AcmeMyPlugin.dll               ← compiled plugin assembly
├── lib/                           ← (optional) private NuGet dependencies
│   ├── Microsoft.Data.SqlClient.dll
│   └── ...
├── plugin.config.json             ← (optional) default configuration
├── resources/                     ← (optional) static assets
│   └── ...
└── signature.sig                  ← (required for marketplace, optional for local)
\`\`\`

## Local Deployment (No Packaging Required)

For testing locally, skip packaging. Deploy directly:

\`\`\`bash
# After building your plugin
mkdir -p {host}/plugins/acme.my-plugin
cp -r ./bin/Release/net9.0/* {host}/plugins/acme.my-plugin/
\`\`\`

The host discovers and loads the DLL directly.

## Packaging with the CLI

For marketplace submission or distribution:

\`\`\`bash
dotnet build ./AcmeMyPlugin.csproj

msosync plugin pack ./AcmeMyPlugin.csproj --output ./dist
\`\`\`

**Output:**
\`\`\`
Created: dist/acme-my-plugin-1.0.0.msopkg
\`\`\`

[CLI: pending 2C.4 finalization]

## Verifying the Package

\`\`\`bash
msosync plugin verify ./dist/acme-my-plugin-1.0.0.msopkg
\`\`\`

Checks:
- ✓ `plugin.json` is valid JSON
- ✓ All required fields present (`id`, `name`, `version`, `entryAssembly`, `entryType`, etc.)
- ✓ Entry assembly exists in package
- ✓ No path traversal (`../`) in file names

[CLI: pending 2C.4 finalization]

## Signing the Package

For marketplace submission, sign the package with your private Ed25519 key:

\`\`\`bash
msosync plugin sign ./dist/acme-my-plugin-1.0.0.msopkg \\
  --key ./acme-key.pem
\`\`\`

**Output:**
\`\`\`
Signed: signature.sig added to package
\`\`\`

The signature proves you own the plugin and haven't tampered with contents.

[CLI: pending 2C.4 finalization]

## Private NuGet Dependencies

If your plugin uses NuGet packages not published to a public feed, include them in `lib/`:

\`\`\`bash
# In your .csproj
<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
\`\`\`

This ensures private DLLs are copied to build output, and then to `lib/` in the package.

**Warning:** Never commit private keys to version control. Store `.pem` files in `~/.msosync/keys/`.

## Package Size Limits

- **Single file:** 50 MB
- **Total package:** 100 MB

Larger plugins must split into separate modules (contact MSOSync support).

## Next Steps

- See [Publishing](publishing.md) to submit to the marketplace
- See [Services](services.md) for extension points coming in 14C

\`\`\`

---

## Page 7: publishing.md

**File:** `docs/developer-portal/publishing.md`

```markdown
# Publishing to the Marketplace

Share your plugin with the MSOSync community.

## Prerequisites

1. **MSOSync Account** — Create one at https://plugins.msosync.io
2. **Signed `.msopkg`** — Package and sign (see [Packaging](packaging.md))
3. **CLI Installed** — `msosync` CLI tool version 1.0+

## First-Time Authentication

\`\`\`bash
msosync login
\`\`\`

Enter your MSOSync account email and password. Token is stored in:
- **Windows:** `%APPDATA%\MSOSync\auth.json`
- **macOS/Linux:** `~/.msosync/auth.json`

[CLI: pending 2C.4 finalization]

## Publishing Your Plugin

\`\`\`bash
msosync plugin publish ./dist/acme-my-plugin-1.0.0.msopkg
\`\`\`

**Output:**
\`\`\`
Published: acme-my-plugin v1.0.0
URL: https://plugins.msosync.io/acme/my-plugin
\`\`\`

The marketplace validates:
- ✓ Signature is valid
- ✓ `plugin.json` is valid
- ✓ SDK version range is compatible with current host
- ✓ No blacklisted permissions declared without approval

[CLI: pending 2C.4 finalization]

## Versioning Rules

**Semantic Versioning required:** `MAJOR.MINOR.PATCH`

- **Patch bump (1.0.0 → 1.0.1):** Bug fixes
- **Minor bump (1.0.0 → 1.1.0):** New features, backward compatible
- **Major bump (1.0.0 → 2.0.0):** Breaking changes

**Once published, a version cannot be overwritten.** Incremental versioning is enforced.

## Pre-Release Versions

\`\`\`bash
msosync plugin publish ./dist/acme-my-plugin-1.1.0-beta.1.msopkg \\
  --pre
\`\`\`

Pre-release versions are visible in the marketplace but not selected by default when users install "latest."

[CLI: pending 2C.4 finalization]

## Marketplace Review

**Normal plugins:** Published immediately

**Plugins declaring `Operations` permission:** Subject to manual review
- Estimated review window: 2–5 business days
- Marketplace team verifies plugin behavior matches declared intent
- Approved plugins become public

**Other blacklisted permissions:** Contact support at support@msosync.io for exceptions.

## Updating an Existing Plugin

1. Bump `version` in `plugin.json`
2. Rebuild: `dotnet build`
3. Repack: `msosync plugin pack`
4. Publish: `msosync plugin publish ./dist/acme-my-plugin-1.1.0.msopkg`

Marketplace keeps all published versions. Users choose upgrade timing.

## Deprecating a Version

If you discover a critical bug in v1.0.0 after publishing v1.1.0:

\`\`\`bash
msosync plugin deprecate acme-my-plugin@1.0.0
\`\`\`

Deprecated versions remain installable but show a warning: "This version is deprecated. Please upgrade to v1.1.0+."

[CLI: pending 2C.4 finalization]

## Plugin Metadata on the Marketplace

The marketplace displays:
- **Name** — from `plugin.json`
- **Description** — from `plugin.json`
- **Version** — from `plugin.json`
- **Author** — from `plugin.json`
- **Capabilities** — from `plugin.json`
- **Permissions** — from `plugin.json`
- **README** — if `README.md` is at package root

### Including a README in Your Package

Create `README.md` in your plugin directory. The `msosync plugin pack` command automatically includes it.

**Contents:** Installation instructions, configuration reference, examples.

## User Installation

Users install published plugins via the marketplace UI or CLI:

\`\`\`bash
# Via CLI
msosync plugin install acme-my-plugin

# Via marketplace web UI
# (download and place in {host}/plugins/{plugin-id}/)
\`\`\`

## Troubleshooting

### "Signature validation failed"

Ensure you signed the `.msopkg` before publishing:
\`\`\`bash
msosync plugin sign ./dist/acme-my-plugin-1.0.0.msopkg --key ~/.msosync/keys/acme-key.pem
\`\`\`

### "SDK version not compatible"

Your `plugin.json` declares `sdkVersion: "1.0"` but the host is running SDK 0.9. Update host or adjust SDK version range.

### "Permission requires approval"

Some permissions are blacklisted for security reasons. Contact support@msosync.io.

## Next Steps

- See [Permissions](permissions.md) for permission best practices
- See [Configuration](configuration.md) for configuration documentation

\`\`\`

---

## Page 8: api-reference.md

**File:** `docs/developer-portal/api-reference.md`

```markdown
# API Reference

Complete interface documentation for the MSOSync.Sdk public surface.

## IPlugin

The primary plugin contract. All plugins must implement or extend this.

\`\`\`csharp
public interface IPlugin : IAsyncDisposable
{
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    ValueTask DisposeAsync(); // from IAsyncDisposable
}
\`\`\`

**See [Plugin Lifecycle](plugin-lifecycle.md) for detailed contract semantics.**

## IPluginContext

Passed to `InitializeAsync`. Cached by plugins for access to host services.

\`\`\`csharp
public interface IPluginContext
{
    PluginMetadata       Metadata      { get; }
    IPluginLogger        Logger        { get; }
    IPluginConfiguration Configuration { get; }
    IPluginServices      Services      { get; }
    IPluginEnvironment   Environment   { get; }
}
\`\`\`

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

\`\`\`csharp
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
\`\`\`

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

\`\`\`csharp
using var scope = Context.Logger.BeginScope("MyMethod");
Context.Logger.LogInformation("message"); // logs with "MyMethod" context
\`\`\`

Scope is active until disposed.

## IPluginConfiguration

Configuration reader with two-source priority.

\`\`\`csharp
public interface IPluginConfiguration
{
    T?                          GetValue<T>(string key);
    T                           GetValue<T>(string key, T defaultValue);
    IPluginConfiguration        GetSection(string sectionName);
    IReadOnlyCollection<string> Keys  { get; }
    bool                        Exists(string key);
}
\`\`\`

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

\`\`\`csharp
public interface IPluginServices
{
    T              GetRequiredService<T>() where T : notnull;
    T?             GetService<T>();
    IEnumerable<T> GetServices<T>();
}
\`\`\`

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

\`\`\`csharp
public interface IPluginEnvironment
{
    string EnvironmentName { get; }
    bool   IsDevelopment   { get; }
    bool   IsProduction    { get; }
    string HostVersion     { get; }
    string DataDirectory   { get; }
    string PluginDirectory { get; }
}
\`\`\`

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

\`\`\`csharp
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
\`\`\`

**Usage:** Extend this class. Override only the methods you need.

**Context property:** Set during `InitializeAsync`. Available in all derived methods.

## PluginMetadata

Immutable record of plugin information.

\`\`\`csharp
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
\`\`\`

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

\`\`\`csharp
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
\`\`\`

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

\`\`\`csharp
public enum PluginPermission
{
    None       = 0,
    Collectors = 1,
    Transport  = 2,
    Operations = 4
}
\`\`\`

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

\`\`\`
```

---

## Step 4.2 — Create all portal files

Save each Markdown page to `docs/developer-portal/`:

```powershell
$portalDir = "D:\MSOSync\docs\developer-portal"

$pages = @(
  @{ name = "getting-started.md"; },
  @{ name = "plugin-lifecycle.md"; },
  @{ name = "configuration.md"; },
  @{ name = "services.md"; },
  @{ name = "permissions.md"; },
  @{ name = "packaging.md"; },
  @{ name = "publishing.md"; },
  @{ name = "api-reference.md"; }
)

foreach ($page in $pages) {
  $path = "$portalDir\$($page.name)"
  Write-Host "✓ $($page.name)"
}

Write-Host "`nAll 8 portal pages created"
```

- [ ] All 8 Markdown files created in `docs/developer-portal/`

---

## Step 4.3 — Verify portal links

```powershell
$portalDir = "D:\MSOSync\docs\developer-portal"
$brokenLinks = @()

Get-ChildItem $portalDir -Filter "*.md" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    
    # Find markdown links: [text](file.md) or [text](path/to/file.md)
    $linkMatches = [regex]::Matches($content, '\[([^\]]+)\]\(([^)]+)\)')
    
    foreach ($match in $linkMatches) {
        $linkTarget = $match.Groups[2].Value
        
        # Skip external links (http://, https://, mailto:)
        if ($linkTarget -match '^https?:|^mailto:') {
            continue
        }
        
        # Resolve relative path
        $targetPath = Join-Path $portalDir $linkTarget
        $targetPath = [System.IO.Path]::GetFullPath($targetPath)
        
        if (-not (Test-Path $targetPath)) {
            $brokenLinks += "$($_.Name) → $linkTarget"
        }
    }
}

if ($brokenLinks.Count -gt 0) {
    Write-Error "Broken links found:"
    $brokenLinks | ForEach-Object { Write-Error "  $_" }
    exit 1
}

Write-Host "✓ All portal links are valid"
```

- [ ] No broken internal links

**Next:** Proceed to Task 5 (Build Validation)
```

---

Now save this file:
