# Epic 2 / Task 1: EF Core Design-Time Tooling

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Install `dotnet-ef` global tool and add `AppDbContextFactory` so EF design-time commands work without a running app host.

**Architecture:** `IDesignTimeDbContextFactory<AppDbContext>` lives in `MSOSync.Persistence`. EF tools discover it via assembly scanning — no startup project needed.

**Tech Stack:** dotnet-ef 9.0.0, EF Core 9.0.0 (packages already in `MSOSync.Persistence.csproj`)

## Global Constraints

- dotnet PATH — prepend before every command:
  ```powershell
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```
- `MSOSync.Persistence.csproj` already has `Microsoft.EntityFrameworkCore.SqlServer`, `.Design`, `.Tools`
- No connection to a real database is needed for `dotnet ef migrations add`

---

**Files:**
- Create: `src/MSOSync.Persistence/AppDbContextFactory.cs`

**Interfaces:**
- Produces: `AppDbContextFactory` — consumed by all subsequent `dotnet ef` commands

---

- [ ] **Step 1: Install dotnet-ef global tool**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet tool install --global dotnet-ef --version 9.0.0
```

Expected: `Tool 'dotnet-ef' (version '9.0.0') was successfully installed.`
(If already installed: `Tool 'dotnet-ef' is already installed.`)

- [ ] **Step 2: Verify tool**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet ef --version
```

Expected: `9.0.0`

- [ ] **Step 3: Create `AppDbContextFactory.cs`**

```csharp
// src/MSOSync.Persistence/AppDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MSOSync.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost,1433;Database=MSOSync;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true;";

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(opts);
    }
}
```

Note: `AppDbContext` does not exist yet — this file will fail to compile until Task 4. That is expected.

- [ ] **Step 4: Verify Persistence project structure compiles (entity + context stubs not yet present — skip build until Task 4)**

No build verification at this step. The file is saved, tool is installed. Proceed to Task 2.

- [ ] **Step 5: Commit**

```powershell
git add src/MSOSync.Persistence/AppDbContextFactory.cs
git commit -m "feat(persistence): add AppDbContextFactory for EF design-time tooling"
```
