# Epic 4 / Task 1: M011 Migration + Entity Cleanup + PrepareToken

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the plaintext `node_token` column (TD002), add `rotation_scheduled` column, update the entity and EF configuration, write `NodeProvisionResult` record, and add transaction-neutral `PrepareToken` to `NodeSecurityService`.

**Architecture:** Pure persistence + security layer change. No service logic beyond staging token rows. `PrepareToken` does NOT call `SaveChangesAsync` — it stages the `SyncNodeSecurity` row on the shared `AppDbContext`. The caller (`NodeMetadataService.ApproveRegistrationAsync` in Task 5) owns the single unit of work.

**Tech Stack:** EF Core 9.0.0 code-first migrations, SQL Server / LocalDB, BCrypt.Net-Next 4.0.3

## Global Constraints

- Migration ID: `"20260619000011_RemovePlaintextNodeToken"`, class `M011_RemovePlaintextNodeToken`
- Namespace: `MSOSync.Persistence.Migrations`
- IF EXISTS guard on DROP COLUMN — resilient against partially-applied migrations
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- dotnet PATH (BOTH required):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Modify: `src/MSOSync.Persistence/Entities/SyncNodeSecurity.cs`
- Modify: `src/MSOSync.Persistence/Configurations/SyncNodeSecurityConfiguration.cs`
- Create: `src/MSOSync.Persistence/Migrations/M011_RemovePlaintextNodeToken.cs`
- Create: `src/MSOSync.Persistence/Migrations/M011_RemovePlaintextNodeToken.Designer.cs`
- Create: `src/MSOSync.Security/NodeProvisionResult.cs`
- Modify: `src/MSOSync.Security/NodeSecurityService.cs`

**Interfaces:**
- Consumes: `AppDbContext.NodeSecurities` DbSet, `BCryptPasswordHasher`, existing `SyncNodeSecurity` entity
- Produces: `NodeProvisionResult(string NodeId, string RawToken)` record + `NodeSecurityService.PrepareToken(string nodeId) → NodeProvisionResult` — consumed by Task 5

---

- [ ] **Step 1: Update `SyncNodeSecurity` entity — remove `NodeToken`, add `RotationScheduled`**

Full file after edit (`src/MSOSync.Persistence/Entities/SyncNodeSecurity.cs`):

```csharp
namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeSecurity
{
    public string NodeId { get; set; } = null!;
    public string CurrentTokenHash { get; set; } = null!;
    public string? NextTokenHash { get; set; }
    public DateTime? RotationScheduled { get; set; }
    public DateTime? CreatedTime { get; set; }
}
```

- [ ] **Step 2: Update `SyncNodeSecurityConfiguration` — remove `NodeToken` mapping, add `RotationScheduled`**

Full file after edit (`src/MSOSync.Persistence/Configurations/SyncNodeSecurityConfiguration.cs`):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeSecurityConfiguration : IEntityTypeConfiguration<SyncNodeSecurity>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeSecurity> builder)
    {
        builder.ToTable("sync_node_security", Schema);
        builder.HasKey(e => e.NodeId);

        builder.Property(e => e.NodeId)
            .HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.CurrentTokenHash)
            .HasColumnName("current_token_hash").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false).IsRequired();
        builder.Property(e => e.NextTokenHash)
            .HasColumnName("next_token_hash").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false);
        builder.Property(e => e.RotationScheduled)
            .HasColumnName("rotation_scheduled").HasColumnType("datetime2(7)");
        builder.Property(e => e.CreatedTime)
            .HasColumnName("created_time").HasColumnType("datetime2(7)");
    }
}
```

- [ ] **Step 3: Build Persistence to verify entity + config compile**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build src\MSOSync.Persistence\MSOSync.Persistence.csproj
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 4: Write `M011_RemovePlaintextNodeToken.cs`**

```csharp
// src/MSOSync.Persistence/Migrations/M011_RemovePlaintextNodeToken.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000011_RemovePlaintextNodeToken")]
public partial class M011_RemovePlaintextNodeToken : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // IF EXISTS guard — resilient against partially migrated DBs
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('msosync.sync_node_security')
                  AND name = 'node_token'
            )
            BEGIN
                ALTER TABLE msosync.sync_node_security DROP COLUMN node_token
            END
            """);

        migrationBuilder.AddColumn<DateTime>(
            name: "rotation_scheduled",
            schema: "msosync",
            table: "sync_node_security",
            type: "datetime2",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "rotation_scheduled",
            schema: "msosync",
            table: "sync_node_security");

        migrationBuilder.AddColumn<string>(
            name: "node_token",
            schema: "msosync",
            table: "sync_node_security",
            type: "varchar(255)",
            unicode: false,
            maxLength: 255,
            nullable: true);
    }
}
```

- [ ] **Step 5: Write `M011_RemovePlaintextNodeToken.Designer.cs`**

```csharp
// src/MSOSync.Persistence/Migrations/M011_RemovePlaintextNodeToken.Designer.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Persistence;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260619000011_RemovePlaintextNodeToken")]
partial class M011_RemovePlaintextNodeToken
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) { }
}
```

- [ ] **Step 6: Create `NodeProvisionResult` record in `MSOSync.Security`**

```csharp
// src/MSOSync.Security/NodeProvisionResult.cs
namespace MSOSync.Security;

public sealed record NodeProvisionResult(string NodeId, string RawToken);
```

- [ ] **Step 7: Add `PrepareToken` to `NodeSecurityService`**

Full file after edit (`src/MSOSync.Security/NodeSecurityService.cs`):

```csharp
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Security;

public sealed class NodeSecurityService(AppDbContext db, BCryptPasswordHasher hasher)
{
    public async Task<bool> ValidateTokenAsync(
        string nodeId, string token, CancellationToken ct = default)
    {
        var sec = await db.NodeSecurities
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);

        if (sec == null) return false;

        if (hasher.Verify(token, sec.CurrentTokenHash)) return true;
        if (sec.NextTokenHash != null && hasher.Verify(token, sec.NextTokenHash)) return true;

        return false;
    }

    public NodeProvisionResult PrepareToken(string nodeId)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = hasher.Hash(raw);

        var existing = db.NodeSecurities.Local.FirstOrDefault(s => s.NodeId == nodeId)
            ?? db.NodeSecurities.Find(nodeId);

        if (existing != null)
        {
            existing.CurrentTokenHash = hash;
            existing.NextTokenHash = null;
            existing.RotationScheduled = null;
        }
        else
        {
            db.NodeSecurities.Add(new SyncNodeSecurity
            {
                NodeId = nodeId,
                CurrentTokenHash = hash,
                CreatedTime = DateTime.UtcNow
            });
        }

        return new NodeProvisionResult(nodeId, raw);
    }
}
```

- [ ] **Step 8: Build full solution to verify zero warnings**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build MSOSync.sln
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 9: Run all existing tests — must still pass**

```powershell
dotnet test MSOSync.sln --no-build
```

Expected: all previously passing tests still pass (61 total: 35 unit + 13 integration + 2 arch + 3 skips)

- [ ] **Step 10: Commit**

```powershell
git add src/MSOSync.Persistence/Entities/SyncNodeSecurity.cs
git add src/MSOSync.Persistence/Configurations/SyncNodeSecurityConfiguration.cs
git add src/MSOSync.Persistence/Migrations/M011_RemovePlaintextNodeToken.cs
git add src/MSOSync.Persistence/Migrations/M011_RemovePlaintextNodeToken.Designer.cs
git add src/MSOSync.Security/NodeProvisionResult.cs
git add src/MSOSync.Security/NodeSecurityService.cs
git commit -m "feat(persistence): M011 remove plaintext node_token, add rotation_scheduled; add PrepareToken"
```
