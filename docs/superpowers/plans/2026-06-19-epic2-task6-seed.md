# Epic 2 / Task 6: M008 Seed Data + Database Smoke Test

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Write M008 seed migration with idempotent `IF NOT EXISTS` inserts, then run `dotnet ef database update` against the Docker SQL Server instance to verify all 8 migrations apply cleanly and seed data is present.

**Architecture:** M008 uses raw SQL (`migrationBuilder.Sql()`) with `IF NOT EXISTS` guards. EF migration history prevents double-apply, but seed guards ensure manual re-runs also stay safe.

**Tech Stack:** EF Core 9.0.0, SQL Server 2022 (Docker)

## Global Constraints

- `IF NOT EXISTS` guard on every seed INSERT
- Docker must be running before smoke test: `docker compose up -d sqlserver`
- SA_PASSWORD from `.env` file (in repo root, not committed — see `.env.example`)
- Connection string for `dotnet ef database update`:
  ```
  Server=localhost,1433;Database=MSOSync;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=true;
  ```
- dotnet PATH:
  ```powershell
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `src/MSOSync.Persistence/Migrations/M008_SeedData.cs`

**Interfaces:**
- Consumes: M001–M007 (Task 5) — sync_role, sync_channel, sync_parameter tables must exist
- Produces: seeded database — consumed by Task 8 (integration tests verify seed data)

---

- [ ] **Step 1: Write M008_SeedData.cs**

```csharp
// src/MSOSync.Persistence/Migrations/M008_SeedData.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000008_SeedData")]
public sealed class M008_SeedData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Roles
        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_role] WHERE [role_name] = 'ADMIN')
    INSERT INTO [msosync].[sync_role] ([role_name]) VALUES ('ADMIN');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_role] WHERE [role_name] = 'OPERATOR')
    INSERT INTO [msosync].[sync_role] ([role_name]) VALUES ('OPERATOR');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_role] WHERE [role_name] = 'VIEWER')
    INSERT INTO [msosync].[sync_role] ([role_name]) VALUES ('VIEWER');
");

        // Default channel
        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_channel] WHERE [channel_id] = 'config')
    INSERT INTO [msosync].[sync_channel]
        ([channel_id], [priority], [batch_size], [max_batch_to_send], [max_data_size], [enabled])
    VALUES ('config', 100, 1000, 10, 1048576, 1);
");

        // Default parameters
        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'sync.interval.seconds')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('sync.interval.seconds', '900');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'retention.days')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('retention.days', '30');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'audit.retention.days')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('audit.retention.days', '90');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'max.retries')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('max.retries', '3');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'heartbeat.interval.seconds')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('heartbeat.interval.seconds', '60');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'queue.warn.threshold')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('queue.warn.threshold', '0.8');
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DELETE FROM [msosync].[sync_parameter]
WHERE [parameter_name] IN (
    'sync.interval.seconds','retention.days','audit.retention.days',
    'max.retries','heartbeat.interval.seconds','queue.warn.threshold');
DELETE FROM [msosync].[sync_channel] WHERE [channel_id] = 'config';
DELETE FROM [msosync].[sync_role] WHERE [role_name] IN ('ADMIN','OPERATOR','VIEWER');
");
    }
}
```

- [ ] **Step 2: Verify 8 migrations listed**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet ef migrations list --project src\MSOSync.Persistence
```

Expected: 8 entries `20260619000001_CreateSchema` through `20260619000008_SeedData`, all `(Pending)`.

- [ ] **Step 3: Start Docker SQL Server**

```powershell
docker compose up -d sqlserver
```

Wait ~30 seconds for the healthcheck to pass, then verify:

```powershell
docker compose ps
```

Expected: `sqlserver` service shows `healthy`.

- [ ] **Step 4: Read SA_PASSWORD from .env and run database update**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
# Read SA_PASSWORD from .env
$saPassword = (Get-Content .env | Where-Object { $_ -match '^SA_PASSWORD=' }) -replace '^SA_PASSWORD=', ''
$connStr = "Server=localhost,1433;Database=MSOSync;User Id=sa;Password=$saPassword;TrustServerCertificate=true;"
dotnet ef database update --project src\MSOSync.Persistence --connection $connStr
```

Expected output ends with:
```
Applying migration '20260619000001_CreateSchema'.
Applying migration '20260619000002_CoreTables'.
...
Applying migration '20260619000008_SeedData'.
Done.
```

- [ ] **Step 5: Verify seed data with sqlcmd**

```powershell
$saPassword = (Get-Content .env | Where-Object { $_ -match '^SA_PASSWORD=' }) -replace '^SA_PASSWORD=', ''
docker exec -it msosync-sqlserver-1 /opt/mssql-tools18/bin/sqlcmd `
  -S localhost -U sa -P $saPassword -C `
  -Q "SELECT COUNT(*) FROM msosync.sync_role; SELECT COUNT(*) FROM msosync.sync_parameter; SELECT COUNT(*) FROM msosync.sync_channel;"
```

Expected: 3 rows in results: `3`, `6`, `1`

- [ ] **Step 6: Commit**

```powershell
git add src/MSOSync.Persistence/Migrations/M008_SeedData.cs
git commit -m "feat(persistence): add M008 seed data migration"
```
