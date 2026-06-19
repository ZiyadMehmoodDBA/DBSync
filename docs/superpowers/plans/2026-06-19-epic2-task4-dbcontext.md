# Epic 2 / Task 4: AppDbContext

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `AppDbContext` with 23 DbSets and `ApplyConfigurationsFromAssembly`. Verify the full `MSOSync.Persistence` project compiles cleanly.

**Architecture:** `AppDbContext` derives from `DbContext`, uses options injection (no parameterless constructor). `OnModelCreating` delegates all mapping to the 23 configuration classes via assembly scan.

**Tech Stack:** EF Core 9.0.0

## Global Constraints

- No `IdentityDbContext` — custom sync_user / sync_role tables
- `sealed` class
- No parameterless constructor
- dotnet PATH:
  ```powershell
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `src/MSOSync.Persistence/AppDbContext.cs`

**Interfaces:**
- Consumes: all 23 entity types (Task 2), `AppDbContextFactory` (Task 1)
- Produces: `AppDbContext` — consumed by Tasks 5–8

---

- [ ] **Step 1: Write `AppDbContext.cs`**

```csharp
// src/MSOSync.Persistence/AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SyncNode> Nodes => Set<SyncNode>();
    public DbSet<SyncNodeGroup> NodeGroups => Set<SyncNodeGroup>();
    public DbSet<SyncNodeSecurity> NodeSecurities => Set<SyncNodeSecurity>();
    public DbSet<SyncRegistrationRequest> RegistrationRequests => Set<SyncRegistrationRequest>();
    public DbSet<SyncChannel> Channels => Set<SyncChannel>();
    public DbSet<SyncTrigger> Triggers => Set<SyncTrigger>();
    public DbSet<SyncTriggerHist> TriggerHists => Set<SyncTriggerHist>();
    public DbSet<SyncRouter> Routers => Set<SyncRouter>();
    public DbSet<SyncTriggerRouter> TriggerRouters => Set<SyncTriggerRouter>();
    public DbSet<SyncDataEvent> DataEvents => Set<SyncDataEvent>();
    public DbSet<SyncDataEventBatch> DataEventBatches => Set<SyncDataEventBatch>();
    public DbSet<SyncOutgoingBatch> OutgoingBatches => Set<SyncOutgoingBatch>();
    public DbSet<SyncIncomingBatch> IncomingBatches => Set<SyncIncomingBatch>();
    public DbSet<SyncBatchError> BatchErrors => Set<SyncBatchError>();
    public DbSet<SyncMonitor> Monitors => Set<SyncMonitor>();
    public DbSet<SyncRuntimeStats> RuntimeStats => Set<SyncRuntimeStats>();
    public DbSet<SyncAudit> Audits => Set<SyncAudit>();
    public DbSet<SyncParameter> Parameters => Set<SyncParameter>();
    public DbSet<SyncParameterHist> ParameterHists => Set<SyncParameterHist>();
    public DbSet<SyncLock> Locks => Set<SyncLock>();
    public DbSet<SyncUser> Users => Set<SyncUser>();
    public DbSet<SyncRole> Roles => Set<SyncRole>();
    public DbSet<SyncUserRole> UserRoles => Set<SyncUserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

- [ ] **Step 2: Verify MSOSync.Persistence builds**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build src\MSOSync.Persistence\MSOSync.Persistence.csproj
```

Expected: `Build succeeded.` with 0 warnings (TreatWarningsAsErrors is on).

- [ ] **Step 3: Verify EF tools can see the context**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet ef dbcontext info --project src\MSOSync.Persistence
```

Expected output includes: `Provider name: Microsoft.EntityFrameworkCore.SqlServer` and `Database name: MSOSync`

- [ ] **Step 4: Commit**

```powershell
git add src/MSOSync.Persistence/AppDbContext.cs
git commit -m "feat(persistence): add AppDbContext with 23 DbSets"
```
