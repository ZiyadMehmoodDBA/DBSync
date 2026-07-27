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
```json
{
  "DatabaseUrl": "localhost:5432",
  "Timeout": 30
}
```

**Host appsettings.json** (high priority):
```json
{
  "Plugins": {
    "acme.my-plugin": {
      "DatabaseUrl": "prod-db.acme.com:5432"
    }
  }
}
```

**Resolution:**
- `GetValue("DatabaseUrl")` returns `"prod-db.acme.com:5432"` (from appsettings.json)
- `GetValue("Timeout")` returns `30` (from plugin.config.json)

This allows deployments to override sensitive values (database URLs, API keys) without modifying the plugin package.

## Reading Scalar Values

```csharp
// With default
var timeout = Context.Configuration.GetValue("Timeout", 30);

// Without default (returns null if not found)
var? url = Context.Configuration.GetValue<string>("DatabaseUrl");
if (url != null)
{
    // use it
}
```

**Supported types:** `string`, `int`, `bool`, `double`, `TimeSpan`

### TimeSpan Parsing

`TimeSpan` values use ISO 8601 format:

```json
{
  "PollingInterval": "00:00:30"
}
```

```csharp
var interval = Context.Configuration.GetValue<TimeSpan>("PollingInterval");
// Result: TimeSpan(30 seconds)
```

## Reading Sections (Nested Config)

```csharp
var retryConfig = Context.Configuration.GetSection("Retry");
var maxAttempts = retryConfig.GetValue("MaxAttempts", 3);
var delayMs = retryConfig.GetValue("DelayMs", 1000);
```

Nesting is arbitrary depth:

```csharp
Context.Configuration
    .GetSection("Database")
    .GetSection("Replica")
    .GetValue<string>("ConnectionString");
```

## Checking Existence

```csharp
if (Context.Configuration.Exists("OptionalFeature"))
{
    EnableOptionalFeature();
}
```

## Enumerating Keys

```csharp
var allKeys = Context.Configuration.Keys;
Context.Logger.LogInformation("Configured keys: {Count}", allKeys.Count);

foreach (var key in allKeys)
{
    Context.Logger.LogDebug("  - {Key}", key);
}
```

**Note:** Key order is undefined. Do not depend on order.

## Typed Configuration Binding Pattern

There is no `Bind<T>()` method in SDK 1.0. Use manual binding:

```csharp
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
```

Call this in `InitializeAsync` to load settings once, or in a timer to implement hot-reload (see below).

## Hot-Reload Workaround

SDK 1.0 does not provide change notifications. To detect config changes at runtime:

```csharp
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
```

This polls every 30 seconds and logs changes. In production, consider longer intervals to reduce overhead.

**Future:** SDK 2.0 will add `IPluginConfigurationMonitor<T>` with reactive change notifications.

## plugin.config.json Format

**Location:** Same directory as the plugin DLL

**Format:** JSON flat or nested objects (no arrays at root)

**Size limit:** 1 MB

**On parse failure:** Host logs warning and ignores the file; configuration comes from appsettings.json only.

### Example: Flat

```json
{
  "Enabled": true,
  "TimeoutSeconds": 30,
  "RetryCount": 3
}
```

### Example: Nested

```json
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
```

## Configuration Override Precedence (Complete)

```
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
```

## Best Practices

- **Validate in `InitializeAsync`:** Throw if critical config is missing
- **Log config values in `InitializeAsync` (dev only):** `if (Context.Environment.IsDevelopment)`
- **Use `GetValue(key, default)` for optional keys:** Always provide a sensible default
- **Use `GetValue<T>(key)` only if key is required:** Document why the key must exist
- **Avoid reading config in hot loops:** Cache at init, poll periodically for changes
- **Use `TimeSpan` type for durations:** Avoids hardcoding seconds/milliseconds
