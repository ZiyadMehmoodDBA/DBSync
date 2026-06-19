# Epic 2 / Task 7: Query Objects + DI Registration + Health Check

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create 7 query objects, `PersistenceServiceExtensions`, and `PersistenceHealthCheck`. Wire `AddPersistence()` into `MSOSync.App`'s `Program.cs`. Verify solution-wide build succeeds.

**Architecture:** Query objects use constructor-injected `AppDbContext` (primary constructor syntax). All queries use `.AsNoTracking()`. DI extension registers `AppDbContext`, all 7 queries, and the health check.

**Tech Stack:** EF Core 9.0.0, ASP.NET Core health checks

## Global Constraints

- `AsNoTracking()` on every query — no exceptions
- `AppDbContext` injected directly — no repository wrapper
- `MigrationsHistoryTable("__EFMigrationsHistory", schema)` — migrations history in the msosync schema
- `MSOSync.App.csproj` must add `<ProjectReference>` to `MSOSync.Persistence`
- dotnet PATH:
  ```powershell
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `src/MSOSync.Persistence/Queries/GetPendingBatchesQuery.cs`
- Create: `src/MSOSync.Persistence/Queries/GetOfflineNodesQuery.cs`
- Create: `src/MSOSync.Persistence/Queries/GetRetryCandidatesQuery.cs`
- Create: `src/MSOSync.Persistence/Queries/GetEventQueueDepthQuery.cs`
- Create: `src/MSOSync.Persistence/Queries/GetNodeByIdQuery.cs`
- Create: `src/MSOSync.Persistence/Queries/GetNodeSecurityQuery.cs`
- Create: `src/MSOSync.Persistence/Queries/GetUserByUsernameQuery.cs`
- Create: `src/MSOSync.Persistence/PersistenceServiceExtensions.cs`
- Create: `src/MSOSync.Persistence/PersistenceHealthCheck.cs`
- Modify: `src/MSOSync.App/MSOSync.App.csproj` (add ProjectReference to Persistence)
- Modify: `src/MSOSync.App/Program.cs` (call `AddPersistence`)

**Interfaces:**
- Consumes: `AppDbContext` (Task 4), entity types (Task 2)
- Produces: `GetPendingBatchesQuery`, `GetOfflineNodesQuery`, `GetRetryCandidatesQuery`, `GetEventQueueDepthQuery`, `GetNodeByIdQuery`, `GetNodeSecurityQuery`, `GetUserByUsernameQuery` — consumed by Task 8 (tests)

---

- [ ] **Step 1: Write 7 query object files**

```csharp
// src/MSOSync.Persistence/Queries/GetPendingBatchesQuery.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetPendingBatchesQuery(AppDbContext db)
{
    public Task<List<SyncOutgoingBatch>> ExecuteAsync(
        string nodeId, string channelId, CancellationToken ct = default)
        => db.OutgoingBatches
            .AsNoTracking()
            .Where(b => b.NodeId == nodeId
                     && b.ChannelId == channelId
                     && (b.Status == 0 || b.Status == 4))
            .OrderBy(b => b.BatchSequence)
            .ToListAsync(ct);
}
```

```csharp
// src/MSOSync.Persistence/Queries/GetOfflineNodesQuery.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetOfflineNodesQuery(AppDbContext db)
{
    public Task<List<SyncNode>> ExecuteAsync(
        int thresholdMinutes, CancellationToken ct = default)
        => db.Nodes
            .AsNoTracking()
            .Where(n => n.LastHeartbeat < DateTime.UtcNow.AddMinutes(-thresholdMinutes)
                     && n.Status == "REGISTERED")
            .ToListAsync(ct);
}
```

```csharp
// src/MSOSync.Persistence/Queries/GetRetryCandidatesQuery.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetRetryCandidatesQuery(AppDbContext db)
{
    public Task<List<SyncOutgoingBatch>> ExecuteAsync(
        int maxRetries, CancellationToken ct = default)
        => db.OutgoingBatches
            .AsNoTracking()
            .Where(b => b.Status == 3
                     && b.RetryCount < maxRetries
                     && b.NextRetryTime <= DateTime.UtcNow)
            .ToListAsync(ct);
}
```

```csharp
// src/MSOSync.Persistence/Queries/GetEventQueueDepthQuery.cs
using Microsoft.EntityFrameworkCore;

namespace MSOSync.Persistence.Queries;

public sealed class GetEventQueueDepthQuery(AppDbContext db)
{
    public Task<Dictionary<string, int>> ExecuteAsync(CancellationToken ct = default)
        => db.DataEvents
            .AsNoTracking()
            .Where(e => !e.IsProcessed)
            .GroupBy(e => e.ChannelId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
}
```

```csharp
// src/MSOSync.Persistence/Queries/GetNodeByIdQuery.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetNodeByIdQuery(AppDbContext db)
{
    public Task<SyncNode?> ExecuteAsync(string nodeId, CancellationToken ct = default)
        => db.Nodes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
}
```

```csharp
// src/MSOSync.Persistence/Queries/GetNodeSecurityQuery.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetNodeSecurityQuery(AppDbContext db)
{
    public Task<SyncNodeSecurity?> ExecuteAsync(string nodeId, CancellationToken ct = default)
        => db.NodeSecurities
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
}
```

```csharp
// src/MSOSync.Persistence/Queries/GetUserByUsernameQuery.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetUserByUsernameQuery(AppDbContext db)
{
    public Task<SyncUser?> ExecuteAsync(string username, CancellationToken ct = default)
        => db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);
}
```

- [ ] **Step 2: Write `PersistenceHealthCheck.cs`**

```csharp
// src/MSOSync.Persistence/PersistenceHealthCheck.cs
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MSOSync.Persistence;

public sealed class PersistenceHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await db.Database.CanConnectAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable", ex);
        }
    }
}
```

- [ ] **Step 3: Write `PersistenceServiceExtensions.cs`**

```csharp
// src/MSOSync.Persistence/PersistenceServiceExtensions.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence.Queries;

namespace MSOSync.Persistence;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required");

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", schema)));

        services.AddScoped<GetPendingBatchesQuery>();
        services.AddScoped<GetOfflineNodesQuery>();
        services.AddScoped<GetRetryCandidatesQuery>();
        services.AddScoped<GetEventQueueDepthQuery>();
        services.AddScoped<GetNodeByIdQuery>();
        services.AddScoped<GetNodeSecurityQuery>();
        services.AddScoped<GetUserByUsernameQuery>();

        services.AddHealthChecks()
            .AddCheck<PersistenceHealthCheck>("database");

        return services;
    }
}
```

- [ ] **Step 4: Add ProjectReference to `MSOSync.App.csproj`**

Open `src/MSOSync.App/MSOSync.App.csproj` and add inside the existing `<ItemGroup>` with other ProjectReferences:

```xml
<ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
```

Full file after edit:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <Description>ASP.NET Core entry point — wires DI, starts BackgroundService workers</Description>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Serilog.AspNetCore" />
    <PackageReference Include="Serilog.Sinks.Console" />
    <PackageReference Include="Serilog.Sinks.File" />
    <PackageReference Include="Serilog.Enrichers.Thread" />
    <PackageReference Include="Serilog.Enrichers.Environment" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Api\MSOSync.Api.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\MSOSync.Scheduler\MSOSync.Scheduler.csproj" />
    <ProjectReference Include="..\MSOSync.Metrics\MSOSync.Metrics.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Call `AddPersistence` in `Program.cs`**

In `src/MSOSync.App/Program.cs`, add the following line after `builder.Services.AddSwaggerGen()` and before `var app = builder.Build()`:

```csharp
builder.Services.AddPersistence(builder.Configuration);
```

The relevant section of `Program.cs` after the change:

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddPersistence(builder.Configuration);
var app = builder.Build();
```

- [ ] **Step 6: Verify solution-wide build**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build MSOSync.sln
```

Expected: `Build succeeded.` with 0 errors. (Warnings-as-errors is on; fix any CS warnings before proceeding.)

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Persistence/Queries/
git add src/MSOSync.Persistence/PersistenceServiceExtensions.cs
git add src/MSOSync.Persistence/PersistenceHealthCheck.cs
git add src/MSOSync.App/MSOSync.App.csproj
git add src/MSOSync.App/Program.cs
git commit -m "feat(persistence): add query objects, DI extension, and health check"
```
