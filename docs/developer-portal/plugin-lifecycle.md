# Plugin Lifecycle Contract

Every MSOSync plugin follows a predictable four-phase lifecycle, from activation to cleanup.

## Lifecycle Diagram

```
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
```

## Phase 1: InitializeAsync

**When:** After plugin assembly loads, before other plugins start

**Contract:**
```csharp
public virtual Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
{
    Context = context; // Cache the context for later use
    return Task.CompletedTask;
}
```

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
```csharp
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
```

## Phase 2: StartAsync

**When:** After all plugins complete `InitializeAsync` (in startup order)

**Contract:**
```csharp
public virtual Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
```

**At this point:**
- All plugins are initialized
- Your `Context` is cached and ready
- Time to start background threads, timers, listeners

**Important:** Do not block `StartAsync`. Return immediately. Use `System.Threading.Timer` or background `Task.Run()` for ongoing work.

**Anti-pattern:**
```csharp
public override Task StartAsync(CancellationToken cancellationToken)
{
    while (true) // ❌ WRONG: Blocks the host startup
    {
        DoWork();
    }
}
```

**Correct pattern:**
```csharp
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
```

## Phase 3: StopAsync

**When:** Host is shutting down (triggered by `Ctrl+C`, service stop, etc.)

**Contract:**
```csharp
public virtual Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
```

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
```csharp
public override Task StopAsync(CancellationToken cancellationToken)
{
    Context.Logger.LogInformation("Stopping plugin");
    
    _workTimer?.Dispose();
    cancellationToken.ThrowIfCancellationRequested();
    
    return Task.CompletedTask;
}
```

## Phase 4: DisposeAsync

**When:** After `StopAsync`, always (regardless of plugin state)

**Contract:**
```csharp
public virtual ValueTask DisposeAsync()
{
    return ValueTask.CompletedTask;
}
```

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
```csharp
public override async ValueTask DisposeAsync()
{
    _workTimer?.Dispose();
    _httpClient?.Dispose();
    await base.DisposeAsync();
}
```

## PluginBase Convenience

The `PluginBase` abstract class provides default implementations:

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
