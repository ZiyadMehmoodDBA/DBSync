# Accessing Host Services

Plugins can request services from the host via `IPluginServices`.

## What is IPluginServices?

A restricted view of the host's dependency injection (DI) container, exposing only services the host explicitly allows plugins to use.

```csharp
public interface IPluginServices
{
    T              GetRequiredService<T>() where T : notnull;
    T?             GetService<T>();
    IEnumerable<T> GetServices<T>();
}
```

**You access it via:**
```csharp
Context.Services.GetRequiredService<IHttpClientFactory>()
```

## GetRequiredService<T>() vs GetService<T>()

| Method | Returns | Behavior | Use case |
|--------|---------|----------|----------|
| `GetRequiredService<T>()` | `T` | Throws `InvalidOperationException` if not registered | Service is **required** for plugin function |
| `GetService<T>()` | `T?` | Returns `null` if not registered | Service is **optional**; fallback available |

### Example: Required Service

```csharp
var logger = Context.Services.GetRequiredService<IPluginLogger>();
logger.LogInformation("required service works");
```

If the host hasn't registered `IPluginLogger`, this throws. But `IPluginLogger` is always registered, so this never fails.

### Example: Optional Service

```csharp
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
```

If the host hasn't registered `IHttpClientFactory`, `GetService<T>()` returns `null`. Your plugin gracefully falls back.

## GetServices<T>()

Returns all registered implementations of a service interface:

```csharp
var loggers = Context.Services.GetServices<IPluginLogger>();
foreach (var logger in loggers)
{
    logger.LogInformation("message");
}
```

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

```csharp
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
```

## Extension Points (Future)

Future phases (14C and beyond) will add interfaces for plugin-to-host communication:

- `IPluginDataCollector` — Register a collector plugin's output service
- `IPluginTransport` — Register a transport plugin's delivery service

When those interfaces are added, they'll be registered as host services and accessible via:

```csharp
var collectors = Context.Services.GetServices<IPluginDataCollector>();
```

For now (SDK 1.0), only the four context services and `IHttpClientFactory` are available.

## Thread Safety

Services are resolved at plugin activation time. It's safe to call `GetService<T>()` / `GetRequiredService<T>()` from any plugin method (and from background threads).

## Anti-Patterns

### Don't cast IPluginServices to IServiceProvider

```csharp
// WRONG
var provider = (IServiceProvider)Context.Services;
var anything = provider.GetService(typeof(object));
```

`IPluginServices` is not castable to the full `IServiceProvider`. It's intentionally restricted.

### Don't store IPluginServices after DisposeAsync

```csharp
private IPluginServices _services; // WRONG: store after DisposeAsync

public override Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
{
    _services = context.Services; // Don't do this
}
```

Resolve services when you need them, not as a cached reference.

### Don't access database contexts via services

Plugins should not directly access the host's database context. Use documented extension point interfaces instead (when available).
