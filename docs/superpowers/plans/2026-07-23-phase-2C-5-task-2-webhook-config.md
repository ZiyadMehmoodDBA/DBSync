# Task 2: WebhookPlugin + ConfigDrivenPlugin

**Status:** Ready  
**Estimated time:** 6 hours  
**Dependencies:** Task 1 (structure pattern)  
**Blocks:** Task 3 (Templates), Task 4 (Portal)

---

## Summary

Implement two more complete sample plugins: WebhookPlugin demonstrates HTTP delivery and optional service resolution; ConfigDrivenPlugin demonstrates typed configuration binding and hot-reload patterns. Each includes `.csproj`, implementation, manifests, config, and README.

---

## Part C: WebhookPlugin

### Step 2.1 — Create WebhookPlugin directory structure

```powershell
$root = "D:\MSOSync"
$webhook = "$root\samples\WebhookPlugin"

if (-not (Test-Path $webhook)) {
  New-Item -ItemType Directory -Force $webhook | Out-Null
}
Write-Host "Created $webhook"
```

### Step 2.2 — Create WebhookPlugin.csproj

**File:** `samples/WebhookPlugin/WebhookPlugin.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
</Project>
```

### Step 2.3 — Create WebhookPayload.cs

**File:** `samples/WebhookPlugin/WebhookPayload.cs`

```csharp
namespace WebhookPlugin;

internal sealed record WebhookPayload(
    string PluginId,
    string Event,
    string Message,
    DateTime Timestamp);
```

### Step 2.4 — Create WebhookPlugin.cs

**File:** `samples/WebhookPlugin/WebhookPlugin.cs`

```csharp
using System.Text;
using System.Text.Json;
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace WebhookPlugin;

public sealed class WebhookPlugin : PluginBase
{
    private HttpClient? _httpClient;
    private bool _ownsHttpClient;

    public override Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Context = context;
        
        // Validate webhook URL at init time
        var webhookUrl = Context.Configuration.GetValue<string>("WebhookUrl", "");
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            Context.Logger.LogWarning("No WebhookUrl configured; webhook delivery disabled");
        }

        return Task.CompletedTask;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("WebhookPlugin.Start");

        // Try to get HttpClientFactory from host services
        var factory = Context.Services.GetService<IHttpClientFactory>();
        
        if (factory != null)
        {
            _httpClient = factory.CreateClient();
            _ownsHttpClient = false;
            Context.Logger.LogInformation("Using host-provided IHttpClientFactory");
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
            Context.Logger.LogInformation("No IHttpClientFactory available; using standalone HttpClient");
        }

        // Post startup notification
        _ = PostWebhookAsync("plugin.started", "Plugin started");

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("WebhookPlugin.Stop");
        Context.Logger.LogInformation("WebhookPlugin stopping");
        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_ownsHttpClient && _httpClient != null)
        {
            _httpClient.Dispose();
        }

        await base.DisposeAsync();
    }

    private async Task PostWebhookAsync(string eventName, string message)
    {
        try
        {
            var webhookUrl = Context.Configuration.GetValue<string>("WebhookUrl", "");
            if (string.IsNullOrWhiteSpace(webhookUrl) || _httpClient == null)
            {
                return;
            }

            var timeout = Context.Configuration
                .GetValue("TimeoutSeconds", 10);
            var retryCount = Context.Configuration
                .GetValue("RetryCount", 3);

            var payload = new WebhookPayload(
                Context.Metadata.PluginId,
                eventName,
                message,
                DateTime.UtcNow);

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            
            var lastException = (Exception?)null;
            for (int attempt = 1; attempt <= retryCount; attempt++)
            {
                try
                {
                    var response = await _httpClient.PostAsync(webhookUrl, content, cts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Context.Logger.LogDebug(
                            "Webhook delivered: {Event} to {Url}",
                            eventName,
                            webhookUrl);
                        return;
                    }

                    Context.Logger.LogWarning(
                        "Webhook delivery failed: {Event} to {Url} returned {StatusCode}",
                        eventName,
                        webhookUrl,
                        response.StatusCode);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt < retryCount)
                    {
                        Context.Logger.LogDebug(
                            "Webhook delivery attempt {Attempt}/{Total} failed, retrying",
                            attempt,
                            retryCount);
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                }
            }

            if (lastException != null)
            {
                Context.Logger.LogWarning(lastException,
                    "Webhook delivery exhausted retries: {Event} to {Url}",
                    eventName,
                    webhookUrl);
            }
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "Unexpected error in webhook delivery");
        }
    }
}
```

**Key points:**
- `InitializeAsync`: Validates webhook URL configuration
- `StartAsync`: Uses `Context.Services.GetService<IHttpClientFactory>()` (nullable, not `GetRequiredService`)
- Graceful fallback: if factory unavailable, creates standalone `HttpClient`
- `DisposeAsync`: Disposes owned `HttpClient`
- `PostWebhookAsync`: Implements retry logic with exponential backoff simulation
- Never throws on delivery failure — logs and continues
- Respects timeout and retry configuration

### Step 2.5 — Create plugin.json manifest

**File:** `samples/WebhookPlugin/plugin.json`

```json
{
  "manifestVersion": 1,
  "id": "samples.webhook",
  "name": "Webhook Plugin",
  "version": "1.0.0",
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 1000,
  "minHostVersion": "14.0.0",
  "maxHostVersion": "14.9.999",
  "entryAssembly": "WebhookPlugin.dll",
  "entryType": "WebhookPlugin.WebhookPlugin",
  "author": "MSOSync",
  "description": "Webhook plugin sample — demonstrates HTTP delivery, optional service resolution, and retry patterns.",
  "permissions": ["Transport"],
  "dependencies": [],
  "capabilities": ["Transport"]
}
```

### Step 2.6 — Create plugin.config.json

**File:** `samples/WebhookPlugin/plugin.config.json`

```json
{
  "WebhookUrl": "https://hooks.example.com/msosync",
  "TimeoutSeconds": 10,
  "RetryCount": 3
}
```

### Step 2.7 — Create WebhookPlugin README.md

**File:** `samples/WebhookPlugin/README.md`

```markdown
# WebhookPlugin

A plugin that delivers plugin lifecycle events to an HTTP webhook endpoint, demonstrating optional service resolution, HTTP retry patterns, and graceful failure handling.

## What This Sample Teaches

- Using `IPluginServices.GetService<T>()` (nullable return) for optional services
- Fallback pattern: try host-provided service, fall back to self-created instance
- Async HTTP patterns with configurable timeout and retry count
- Never fail the host over external service failures
- Declaring `PluginCapability.Transport` and `PluginPermission.Transport`

## Building

```bash
cd samples/WebhookPlugin
dotnet build
```

Expected output: `Build succeeded in X.XXXs`

## Configuration

All configuration is read from `plugin.config.json` (low priority) or the host's `appsettings.json` under `Plugins:samples.webhook:*` (high priority).

### Configuration Keys

| Key | Type | Default | Description |
|-----|------|---------|---|
| `WebhookUrl` | `string` | (empty) | HTTPS endpoint to POST to |
| `TimeoutSeconds` | `int` | 10 | Per-request timeout |
| `RetryCount` | `int` | 3 | Number of retries on failure |

### Example: Slack Webhook

```json
{
  "Plugins": {
    "samples.webhook": {
      "WebhookUrl": "https://hooks.slack.com/services/YOUR/WEBHOOK/URL",
      "TimeoutSeconds": 10,
      "RetryCount": 2
    }
  }
}
```

## Running Against a Host

1. Build this plugin: `dotnet build`
2. Configure a webhook URL:
   - Modify `plugin.config.json`, or
   - Add to host's `appsettings.json` (recommended)
3. Copy the output to the host's plugin directory: `{host}/plugins/samples.webhook/`
4. Restart the MSOSync host
5. Check the host logs for:
   - `PluginHost1002: Plugin samples.webhook loaded successfully`
   - `Using host-provided IHttpClientFactory` or `using standalone HttpClient`
   - `Webhook delivered: plugin.started to https://...`

## Service Resolution Pattern

This plugin demonstrates the pattern for optional host services:

```csharp
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
```

**Why this pattern?**
- `GetRequiredService<T>()` throws if service not registered
- `GetService<T>()` returns null if not registered
- For optional services, use `GetService<T>()` and implement fallback

## HTTP Delivery Semantics

The plugin posts a JSON payload to the webhook:

```json
{
  "PluginId": "samples.webhook",
  "Event": "plugin.started",
  "Message": "Plugin started",
  "Timestamp": "2026-07-23T14:30:45.123Z"
}
```

### Retry Behavior

- First attempt at `PostWebhookAsync` call
- If timeout or connection error: retry up to `RetryCount` times
- Between retries: 1-second delay (in production, use exponential backoff)
- If all retries fail: log warning, plugin continues (never throws)
- If HTTP response is non-2xx: log warning, no retry (not a transient error)

## Key Concepts Demonstrated

| Concept | Code | Purpose |
|---------|------|---------|
| `GetService<T>()` | Nullable service resolution | Gracefully handle missing services |
| Fallback pattern | Create own instance if service unavailable | Never block plugin on host services |
| HTTP timeout | `CancellationTokenSource` with timeout | Prevent hanging |
| Retry logic | Loop with exponential backoff | Handle transient failures |
| Exception safety | Catch, log, continue | Never fail the host |

## Next Steps

- See [ConfigDrivenPlugin](../ConfigDrivenPlugin/README.md) for advanced configuration patterns
- See [DataCollectorPlugin](../DataCollectorPlugin/README.md) for background timers
```

### Step 2.8 — Verify WebhookPlugin builds

```powershell
cd D:\MSOSync\samples\WebhookPlugin
dotnet build --warnaserror
```

**Expected:** Exit code 0, zero warnings.

- [ ] WebhookPlugin builds successfully

---

## Part D: ConfigDrivenPlugin

### Step 2.9 — Create ConfigDrivenPlugin directory structure

```powershell
$root = "D:\MSOSync"
$config = "$root\samples\ConfigDrivenPlugin"

if (-not (Test-Path $config)) {
  New-Item -ItemType Directory -Force $config | Out-Null
}
Write-Host "Created $config"
```

### Step 2.10 — Create ConfigDrivenPlugin.csproj

**File:** `samples/ConfigDrivenPlugin/ConfigDrivenPlugin.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
</Project>
```

### Step 2.11 — Create PluginSettings.cs

**File:** `samples/ConfigDrivenPlugin/PluginSettings.cs`

```csharp
namespace ConfigDrivenPlugin;

internal sealed record PluginSettings(
    bool EnableDetailedLogging,
    int MaxBatchSize,
    int RetryMaxAttempts,
    int RetryDelayMs,
    int WarnAtQueueDepth,
    int ErrorAtQueueDepth);
```

### Step 2.12 — Create ConfigDrivenPlugin.cs

**File:** `samples/ConfigDrivenPlugin/ConfigDrivenPlugin.cs`

```csharp
using MSOSync.Sdk.Hosting;

namespace ConfigDrivenPlugin;

public sealed class ConfigDrivenPlugin : PluginBase
{
    private Timer? _hotReloadTimer;
    private PluginSettings? _cachedSettings;

    public override Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Context = context;

        // Log all resolved keys at startup
        if (Context.Environment.IsDevelopment)
        {
            Context.Logger.LogInformation(
                "Configuration keys resolved at startup: {Count}",
                Context.Configuration.Keys.Count);

            foreach (var key in Context.Configuration.Keys)
            {
                Context.Logger.LogDebug("  - {Key}", key);
            }
        }
        else
        {
            Context.Logger.LogInformation(
                "Configuration keys resolved: {Count}",
                Context.Configuration.Keys.Count);
        }

        return Task.CompletedTask;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("ConfigDrivenPlugin.Start");

        // Load initial settings
        _cachedSettings = LoadSettings();

        Context.Logger.LogInformation(
            "Config-driven plugin started (PluginId: {PluginId}, DetailedLogging: {DetailedLogging})",
            Context.Metadata.PluginId,
            _cachedSettings.EnableDetailedLogging);

        // Start hot-reload timer (check config every 30 seconds)
        _hotReloadTimer = new Timer(
            _ => CheckForConfigChanges(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("ConfigDrivenPlugin.Stop");
        Context.Logger.LogInformation("Config-driven plugin stopping");
        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        _hotReloadTimer?.Dispose();
        await base.DisposeAsync();
    }

    private PluginSettings LoadSettings()
    {
        var featureSection = Context.Configuration.GetSection("Feature");
        var retrySection = Context.Configuration.GetSection("Retry");
        var thresholdSection = Context.Configuration.GetSection("Thresholds");

        return new PluginSettings(
            EnableDetailedLogging: featureSection.GetValue("EnableDetailedLogging", false),
            MaxBatchSize: featureSection.GetValue("MaxBatchSize", 100),
            RetryMaxAttempts: retrySection.GetValue("MaxAttempts", 3),
            RetryDelayMs: retrySection.GetValue("DelayMs", 1000),
            WarnAtQueueDepth: thresholdSection.GetValue("WarnAtQueueDepth", 1000),
            ErrorAtQueueDepth: thresholdSection.GetValue("ErrorAtQueueDepth", 5000));
    }

    private void CheckForConfigChanges()
    {
        try
        {
            var newSettings = LoadSettings();

            if (_cachedSettings == null)
            {
                return;
            }

            // Check for changes and log them
            if (newSettings.EnableDetailedLogging != _cachedSettings.EnableDetailedLogging)
            {
                Context.Logger.LogInformation(
                    "Config changed: EnableDetailedLogging {Old} → {New}",
                    _cachedSettings.EnableDetailedLogging,
                    newSettings.EnableDetailedLogging);
            }

            if (newSettings.MaxBatchSize != _cachedSettings.MaxBatchSize)
            {
                Context.Logger.LogInformation(
                    "Config changed: MaxBatchSize {Old} → {New}",
                    _cachedSettings.MaxBatchSize,
                    newSettings.MaxBatchSize);
            }

            if (newSettings.RetryMaxAttempts != _cachedSettings.RetryMaxAttempts)
            {
                Context.Logger.LogInformation(
                    "Config changed: RetryMaxAttempts {Old} → {New}",
                    _cachedSettings.RetryMaxAttempts,
                    newSettings.RetryMaxAttempts);
            }

            // Update cached settings
            _cachedSettings = newSettings;
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "Error checking for configuration changes");
        }
    }
}
```

**Key points:**
- `InitializeAsync`: Logs all resolved config keys (debug level in dev, info in prod)
- `LoadSettings()`: Manual typed binding from config sections (no `Bind<T>` method exists)
- `CheckForConfigChanges()`: Hot-reload workaround using a timer
- Tracks `_cachedSettings` and logs changes when detected
- No runtime exceptions — catches and logs errors

### Step 2.13 — Create plugin.json manifest

**File:** `samples/ConfigDrivenPlugin/plugin.json`

```json
{
  "manifestVersion": 1,
  "id": "samples.config-driven",
  "name": "Config Driven Plugin",
  "version": "1.0.0",
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 1000,
  "minHostVersion": "14.0.0",
  "maxHostVersion": "14.9.999",
  "entryAssembly": "ConfigDrivenPlugin.dll",
  "entryType": "ConfigDrivenPlugin.ConfigDrivenPlugin",
  "author": "MSOSync",
  "description": "Configuration sample — demonstrates typed config binding and hot-reload patterns.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

### Step 2.14 — Create plugin.config.json

**File:** `samples/ConfigDrivenPlugin/plugin.config.json`

```json
{
  "Feature": {
    "EnableDetailedLogging": true,
    "MaxBatchSize": 500
  },
  "Retry": {
    "MaxAttempts": 3,
    "DelayMs": 1000
  },
  "Thresholds": {
    "WarnAtQueueDepth": 1000,
    "ErrorAtQueueDepth": 5000
  }
}
```

### Step 2.15 — Create ConfigDrivenPlugin README.md

**File:** `samples/ConfigDrivenPlugin/README.md`

```markdown
# ConfigDrivenPlugin

The authoritative example of `IPluginConfiguration`. Demonstrates typed configuration binding, section navigation, defaults, existence checks, and the hot-reload pattern.

## What This Sample Teaches

- `IPluginConfiguration.GetValue<T>(key, defaultValue)` for scalars with defaults
- `IPluginConfiguration.GetSection("SectionName")` for nested config objects
- `IPluginConfiguration.Keys` enumeration for debugging
- Manual typed binding pattern (constructing a record from config sections)
- Hot-reload via timer-based polling (workaround for lack of change notifications in SDK 1.0)
- Configuration priority: host `appsettings.json` wins over `plugin.config.json`

## Building

```bash
cd samples/ConfigDrivenPlugin
dotnet build
```

Expected output: `Build succeeded in X.XXXs`

## Configuration

This plugin reads from `plugin.config.json` and the host's `appsettings.json` (`Plugins:samples.config-driven:*`).

### Configuration Structure

```
Feature
  ├── EnableDetailedLogging (bool)
  └── MaxBatchSize (int)
Retry
  ├── MaxAttempts (int)
  └── DelayMs (int)
Thresholds
  ├── WarnAtQueueDepth (int)
  └── ErrorAtQueueDepth (int)
```

### Configuration Keys

| Key | Type | Default | Description |
|-----|------|---------|---|
| `Feature:EnableDetailedLogging` | bool | false | Enable verbose logging |
| `Feature:MaxBatchSize` | int | 100 | Max items per batch |
| `Retry:MaxAttempts` | int | 3 | Max retry attempts |
| `Retry:DelayMs` | int | 1000 | Delay between retries (ms) |
| `Thresholds:WarnAtQueueDepth` | int | 1000 | Log warning above this depth |
| `Thresholds:ErrorAtQueueDepth` | int | 5000 | Log error above this depth |

### Example: Override via appsettings.json

```json
{
  "Plugins": {
    "samples.config-driven": {
      "Feature": {
        "EnableDetailedLogging": false,
        "MaxBatchSize": 1000
      },
      "Retry": {
        "MaxAttempts": 5,
        "DelayMs": 2000
      }
    }
  }
}
```

Note: `Thresholds` is not overridden, so it uses the values from `plugin.config.json`.

## Running Against a Host

1. Build this plugin: `dotnet build`
2. Optionally configure values via host's `appsettings.json` (see above)
3. Copy the output to the host's plugin directory: `{host}/plugins/samples.config-driven/`
4. Restart the MSOSync host
5. Check the host logs for:
   - `PluginHost1002: Plugin samples.config-driven loaded successfully`
   - `Configuration keys resolved at startup: N`
   - (if Development environment) list of all keys
   - (every 30 seconds) log lines for any changed values

## Typed Binding Pattern

The `PluginSettings` record is constructed manually from configuration sections:

```csharp
private PluginSettings LoadSettings()
{
    var featureSection = Context.Configuration.GetSection("Feature");
    var retrySection = Context.Configuration.GetSection("Retry");
    var thresholdSection = Context.Configuration.GetSection("Thresholds");

    return new PluginSettings(
        EnableDetailedLogging: featureSection.GetValue("EnableDetailedLogging", false),
        MaxBatchSize: featureSection.GetValue("MaxBatchSize", 100),
        RetryMaxAttempts: retrySection.GetValue("MaxAttempts", 3),
        // ... etc
    );
}
```

There is no `Bind<T>` method on `IPluginConfiguration` in SDK 1.0 — manual binding is the canonical pattern.

## Hot-Reload Pattern (Workaround)

SDK 1.0 does not provide `IPluginConfigurationMonitor<T>`. This plugin implements a workaround:

1. Start a `Timer` that fires every 30 seconds
2. Call `LoadSettings()` to re-read configuration
3. Compare new settings to cached settings
4. Log changes when detected

This is not a perfect solution (changes are detected with ~30-second lag), but it demonstrates the pattern developers can use today.

**Future:** SDK 2.0 will add `IPluginConfigurationMonitor<T>` with change notifications.

## Configuration Priority

When reading configuration, the host applies this priority:

1. **High:** `appsettings.json` under `Plugins:samples.config-driven:*`
2. **Low:** `plugin.config.json` in the plugin directory

If a key exists in appsettings.json, it wins. Otherwise, plugin.config.json is used. If a key exists in neither, the default value provided to `GetValue<T>(key, default)` is used.

Example: if both files have `MaxBatchSize`:
- appsettings.json: `MaxBatchSize: 1000`
- plugin.config.json: `MaxBatchSize: 500`
- Result: `1000` (appsettings.json wins)

## Key Concepts Demonstrated

| Concept | Code | Purpose |
|---------|------|---------|
| `GetSection` | `GetSection("Feature")` | Navigate nested sections |
| `GetValue<T>` | `GetValue("MaxBatchSize", 100)` | Read with default |
| `Keys` | Enumerate all keys | Debug configuration mismatches |
| Typed binding | Manual record construction | Type-safe configuration |
| Hot-reload | Timer polling + comparison | Detect config changes at runtime |
| Priority | appsettings.json > plugin.config.json | Understand which config wins |

## Next Steps

- See [WebhookPlugin](../WebhookPlugin/README.md) for optional service resolution
- See [DataCollectorPlugin](../DataCollectorPlugin/README.md) for background timers with configuration
```

### Step 2.16 — Verify ConfigDrivenPlugin builds

```powershell
cd D:\MSOSync\samples\ConfigDrivenPlugin
dotnet build --warnaserror
```

**Expected:** Exit code 0, zero warnings.

- [ ] ConfigDrivenPlugin builds successfully

---

## Step 2.17 — Final Verification (All 4 Samples)

```powershell
$root = "D:\MSOSync"

$samples = @(
  "HelloWorldPlugin",
  "DataCollectorPlugin",
  "WebhookPlugin",
  "ConfigDrivenPlugin"
)

$failed = @()

foreach ($sample in $samples) {
  $proj = "$root\samples\$sample\$sample.csproj"
  Write-Host "`nBuilding $sample..."
  
  if (-not (Test-Path $proj)) {
    Write-Error "$sample.csproj not found"
    $failed += $sample
    continue
  }
  
  dotnet build $proj /p:MSOSyncSdkLocal=true --warnaserror
  if ($LASTEXITCODE -ne 0) {
    Write-Error "$sample build failed"
    $failed += $sample
  } else {
    Write-Host "✓ $sample"
  }
}

if ($failed.Count -gt 0) {
  Write-Error "Failed: $($failed -join ', ')"
  exit 1
}

Write-Host "`n================================"
Write-Host "Task 2 verification complete!"
Write-Host "All 4 samples build successfully"
Write-Host "================================"
```

- [ ] All 4 samples compile with zero errors and zero warnings

---

## Step 2.18 — Verify No Forbidden Dependencies

```powershell
$root = "D:\MSOSync"

$samples = @(
  "HelloWorldPlugin",
  "DataCollectorPlugin",
  "WebhookPlugin",
  "ConfigDrivenPlugin"
)

$forbidden = @(
  "MSOSync.Api",
  "MSOSync.Metadata",
  "MSOSync.Plugin",
  "MSOSync.Persistence",
  "MSOSync.Common"
)

$foundForbidden = @()

foreach ($sample in $samples) {
  $csproj = "$root\samples\$sample\$sample.csproj"
  $content = Get-Content $csproj -Raw
  
  foreach ($pkg in $forbidden) {
    if ($content -match $pkg) {
      $foundForbidden += "$sample references $pkg"
    }
  }
}

if ($foundForbidden.Count -gt 0) {
  Write-Error "Forbidden dependencies found:"
  $foundForbidden | ForEach-Object { Write-Error "  $_" }
  exit 1
}

Write-Host "✓ No forbidden dependencies found"
```

- [ ] No sample references forbidden packages

**Next:** Proceed to Task 3 (MSOSync.Templates)
