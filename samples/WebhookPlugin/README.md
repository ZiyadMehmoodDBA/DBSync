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
