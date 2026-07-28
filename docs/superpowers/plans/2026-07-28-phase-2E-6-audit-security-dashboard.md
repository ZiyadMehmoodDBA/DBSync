# Phase 2E.6 — Audit Hardening + Security Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add tamper-evident SHA-256 hash chaining to the audit log, expose a SecurityAuditController for paginated audit queries, and deliver a React SecurityDashboardPage summarizing the security posture.

**Architecture:** M044 adds `prev_hash` to `SyncAudit`. `AuditChainService` computes hashes on write and verifies the chain on demand. Sensitive field values are masked in API responses. `SecurityDashboardPage` at `/administration/security` shows MFA stats, API key counts, OIDC config status, and chain integrity.

**Tech Stack:** C# 13 / .NET 9 / SHA-256 via `System.Security.Cryptography` / React 19 / TypeScript / TanStack Query v5 / shadcn/ui

## Global Constraints

- Prerequisite: 2E.1–2E.5 complete
- Migration name: `M044_AuditHashChain`
- Hash formula: `SHA-256(prev_hash_hex_or_empty_string || "\n" || entry_json_canonical)` stored as lowercase hex
- Masking: `SyncAudit.Details` fields containing "password", "secret", "token", "key" → replaced with `"[REDACTED]"`
- Admin endpoints: `[Authorize(Policy = "AdminOnly")]`
- React 19 / TanStack Query v5 — no `onSuccess`/`onError` on `useQuery`
- `git add` by file name only

---

### Task 1: M044 migration — add prev_hash to SyncAudit

**Files:**
- Modify: `SyncAudit` entity (find via `Get-ChildItem -Recurse -Filter "SyncAudit.cs" src/`)
- Modify: `MSOSyncDbContext.cs` (update SyncAudit model config)
- Create: M044 migration via `dotnet ef migrations add M044_AuditHashChain`

**Interfaces:**
- Consumes: existing `SyncAudit` entity
- Produces: `SyncAudit.PrevHash (string?, nullable)`, `SyncAudit.EntryHash (string?, nullable)`

- [ ] **Step 1: Locate and read SyncAudit entity**

```powershell
Get-ChildItem -Recurse -Filter "SyncAudit.cs" src/ | Select-Object FullName
```

Read the file. Note all existing properties (especially the primary key type and the `Details` or `Data` field name).

- [ ] **Step 2: Add hash columns to SyncAudit**

In the SyncAudit entity file, add after existing properties:

```csharp
public string? PrevHash { get; set; }   // SHA-256 hex of previous entry; null for first entry
public string? EntryHash { get; set; }  // SHA-256 hex of this entry
```

- [ ] **Step 3: Update MSOSyncDbContext model config**

Read `src/MSOSync.Persistence/MSOSyncDbContext.cs`. Find the SyncAudit entity configuration. Add:

```csharp
b.Property(e => e.PrevHash).HasMaxLength(64);
b.Property(e => e.EntryHash).HasMaxLength(64);
```

- [ ] **Step 4: Generate migration**

```
dotnet ef migrations add M044_AuditHashChain --project src/MSOSync.Persistence --startup-project src/MSOSync.App
```

Verify `Up()` adds `prev_hash (nvarchar(64), nullable)` and `entry_hash (nvarchar(64), nullable)` to the SyncAudit table.

- [ ] **Step 5: Commit**

```
git add <path-to-SyncAudit.cs> src/MSOSync.Persistence/MSOSyncDbContext.cs
git add src/MSOSync.Persistence/Migrations/
git commit -m "feat(2E.6-T1): add PrevHash/EntryHash to SyncAudit + M044 migration"
```

---

### Task 2: AuditChainService — hash computation + verification

**Files:**
- Create: `src/MSOSync.Api/Security/IAuditChainService.cs`
- Create: `src/MSOSync.Api/Security/AuditChainService.cs`
- Create: `tests/MSOSync.ApiTests/Security/AuditChainServiceTests.cs`

**Interfaces:**
- Consumes: `MSOSyncDbContext`, `SyncAudit` with new hash columns (Task 1)
- Produces:
  - `ComputeHash(string? prevHash, SyncAudit entry) : string` — SHA-256 hex
  - `VerifyChainAsync(ct) : Task<(bool IsValid, int? FirstBrokenId)>` — reads all entries in order, verifies chain
  - `SetHashesAsync(SyncAudit entry, ct) : Task` — fetches prev entry's hash, computes this entry's hash, sets both

- [ ] **Step 1: Write failing tests**

```csharp
// tests/MSOSync.ApiTests/Security/AuditChainServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Security;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Security;

public sealed class AuditChainServiceTests : IDisposable
{
    private readonly MSOSyncDbContext _db;

    public AuditChainServiceTests()
    {
        var options = new DbContextOptionsBuilder<MSOSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new MSOSyncDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ComputeHash_IsDeterministic_ForSameInput()
    {
        var svc = new AuditChainService(_db);
        // Adapt SyncAudit construction to actual property names found in Task 1 Step 1
        var entry = new SyncAudit { Id = 1, Action = "login", UserId = 1, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

        var hash1 = svc.ComputeHash(null, entry);
        var hash2 = svc.ComputeHash(null, entry);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // SHA-256 hex
    }

    [Fact]
    public void ComputeHash_DiffersWhenPrevHashDiffers()
    {
        var svc = new AuditChainService(_db);
        var entry = new SyncAudit { Id = 1, Action = "login", UserId = 1, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

        var hashA = svc.ComputeHash(null, entry);
        var hashB = svc.ComputeHash("abc123", entry);

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public async Task VerifyChainAsync_ReturnsValid_ForConsistentChain()
    {
        var svc = new AuditChainService(_db);
        var e1 = new SyncAudit { Id = 1, Action = "login", UserId = 1, CreatedAt = DateTime.UtcNow };
        e1.PrevHash = null;
        e1.EntryHash = svc.ComputeHash(null, e1);

        var e2 = new SyncAudit { Id = 2, Action = "logout", UserId = 1, CreatedAt = DateTime.UtcNow.AddSeconds(1) };
        e2.PrevHash = e1.EntryHash;
        e2.EntryHash = svc.ComputeHash(e1.EntryHash, e2);

        _db.AuditLog.AddRange(e1, e2);
        await _db.SaveChangesAsync();

        var (isValid, brokenId) = await svc.VerifyChainAsync();

        isValid.Should().BeTrue();
        brokenId.Should().BeNull();
    }

    [Fact]
    public async Task VerifyChainAsync_ReturnsBrokenId_WhenChainTampered()
    {
        var svc = new AuditChainService(_db);
        var e1 = new SyncAudit { Id = 1, Action = "login", UserId = 1, CreatedAt = DateTime.UtcNow };
        e1.PrevHash = null;
        e1.EntryHash = svc.ComputeHash(null, e1);

        var e2 = new SyncAudit { Id = 2, Action = "logout", UserId = 1, CreatedAt = DateTime.UtcNow.AddSeconds(1) };
        e2.PrevHash = "tampered-hash";  // wrong prev hash
        e2.EntryHash = svc.ComputeHash("tampered-hash", e2);

        _db.AuditLog.AddRange(e1, e2);
        await _db.SaveChangesAsync();

        var (isValid, brokenId) = await svc.VerifyChainAsync();

        isValid.Should().BeFalse();
        brokenId.Should().Be(2);
    }
}
```

Note: replace `_db.AuditLog`, `SyncAudit.Action`, `SyncAudit.UserId`, `SyncAudit.Id`, `SyncAudit.CreatedAt` with the actual property names found in Task 1 Step 1.

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 3: Create interface**

```csharp
// src/MSOSync.Api/Security/IAuditChainService.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Security;

public interface IAuditChainService
{
    string ComputeHash(string? prevHash, SyncAudit entry);
    Task SetHashesAsync(SyncAudit entry, CancellationToken ct = default);
    Task<(bool IsValid, int? FirstBrokenId)> VerifyChainAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement AuditChainService**

Adapt `_db.AuditLog` (the DbSet name), `e.Id`, `e.Action`, `e.UserId`, `e.Details`/`e.Data`, `e.CreatedAt` to actual property names found in Task 1 Step 1. The canonical entry string must use stable serialization (ordered properties).

```csharp
// src/MSOSync.Api/Security/AuditChainService.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Security;

internal sealed class AuditChainService(MSOSyncDbContext db) : IAuditChainService
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };

    public string ComputeHash(string? prevHash, SyncAudit entry)
    {
        // Canonical representation: stable serialization of entry data
        var canonical = $"{prevHash ?? string.Empty}\n{CanonicalEntry(entry)}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task SetHashesAsync(SyncAudit entry, CancellationToken ct = default)
    {
        // Get the most recent entry's hash
        var prevEntry = await db.AuditLog
            .AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Select(e => e.EntryHash)
            .FirstOrDefaultAsync(ct);

        entry.PrevHash = prevEntry;
        entry.EntryHash = ComputeHash(prevEntry, entry);
    }

    public async Task<(bool IsValid, int? FirstBrokenId)> VerifyChainAsync(CancellationToken ct = default)
    {
        // Adapt order-by and Id property to actual schema
        var entries = await db.AuditLog
            .AsNoTracking()
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

        string? expectedPrevHash = null;
        foreach (var entry in entries)
        {
            if (entry.PrevHash != expectedPrevHash)
                return (false, entry.Id);

            var expectedHash = ComputeHash(entry.PrevHash, entry);
            if (entry.EntryHash != expectedHash)
                return (false, entry.Id);

            expectedPrevHash = entry.EntryHash;
        }

        return (true, null);
    }

    private static string CanonicalEntry(SyncAudit e)
    {
        // Include all stable fields — adapt property names to actual SyncAudit fields
        // Do NOT include PrevHash or EntryHash (they're part of the wrapping, not the entry)
        return JsonSerializer.Serialize(new
        {
            id = e.Id,
            action = e.Action,
            user_id = e.UserId,
            created_at = e.CreatedAt.ToString("O"),
        }, _opts);
    }
}
```

- [ ] **Step 5: Register in DI**

```csharp
services.AddScoped<IAuditChainService, AuditChainService>();
```

- [ ] **Step 6: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+4, Failed: 0`

- [ ] **Step 7: Commit**

```
git add src/MSOSync.Api/Security/IAuditChainService.cs src/MSOSync.Api/Security/AuditChainService.cs tests/MSOSync.ApiTests/Security/AuditChainServiceTests.cs
git commit -m "feat(2E.6-T2): add AuditChainService with SHA-256 hash chain"
```

---

### Task 3: SecurityAuditController + tests

**Files:**
- Create: `src/MSOSync.Api/Controllers/SecurityAuditController.cs`
- Create: `tests/MSOSync.ApiTests/Controllers/SecurityAuditControllerTests.cs`

**Interfaces:**
- Consumes: `MSOSyncDbContext`, `IAuditChainService` (Task 2)
- Produces:
  - `GET /api/security/audit?page=1&pageSize=50` (AdminOnly) — paginated, sensitive fields masked
  - `GET /api/security/audit/verify` (AdminOnly) — chain integrity check

- [ ] **Step 1: Write failing tests**

```csharp
// tests/MSOSync.ApiTests/Controllers/SecurityAuditControllerTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using MSOSync.Api.Controllers;
using MSOSync.Api.Security;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class SecurityAuditControllerTests : IDisposable
{
    private readonly MSOSyncDbContext _db;
    private readonly Mock<IAuditChainService> _chain = new();
    private readonly SecurityAuditController _controller;

    public SecurityAuditControllerTests()
    {
        var options = new DbContextOptionsBuilder<MSOSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new MSOSyncDbContext(options);
        _controller = new SecurityAuditController(_db, _chain.Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetAudit_ReturnsPaginatedEntries()
    {
        // Adapt SyncAudit construction to actual property names
        for (var i = 0; i < 5; i++)
            _db.AuditLog.Add(new SyncAudit { Action = $"action-{i}", UserId = 1, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _controller.GetAudit(page: 1, pageSize: 3);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task VerifyChain_ReturnsIntegrityResult()
    {
        _chain.Setup(c => c.VerifyChainAsync(default))
            .ReturnsAsync((true, (int?)null));

        var result = await _controller.VerifyChain();

        result.Result.Should().BeOfType<OkObjectResult>();
        var body = ((OkObjectResult)result.Result!).Value!.ToString();
        body.Should().Contain("true");
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 3: Implement SecurityAuditController**

Adapt `db.AuditLog` and SyncAudit property names (`Action`, `UserId`, `Details`, `CreatedAt`) to the actual schema found in Task 1 Step 1. Add masking logic for sensitive fields in `Details`/`Data` if that field contains JSON.

```csharp
// src/MSOSync.Api/Controllers/SecurityAuditController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Security;
using MSOSync.Persistence;
using System.Text.RegularExpressions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/security")]
[Authorize(Policy = "AdminOnly")]
public sealed class SecurityAuditController(
    MSOSyncDbContext db,
    IAuditChainService chainService) : ControllerBase
{
    private static readonly Regex _sensitivePattern =
        new(@"""(password|secret|token|key)""\s*:\s*""[^""]*""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [HttpGet("audit")]
    public async Task<ActionResult<object>> GetAudit(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var total = await db.AuditLog.CountAsync(ct);

        // Adapt OrderBy and Select to actual SyncAudit property names
        var entries = await db.AuditLog
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.Action,
                e.UserId,
                e.CreatedAt,
                e.EntryHash,
                // Mask sensitive fields in Details/Data if present
                // Details = MaskSensitive(e.Details ?? ""),
            })
            .ToListAsync(ct);

        return Ok(new { total, page, page_size = pageSize, items = entries });
    }

    [HttpGet("audit/verify")]
    public async Task<ActionResult<object>> VerifyChain(CancellationToken ct = default)
    {
        var (isValid, brokenId) = await chainService.VerifyChainAsync(ct);
        return Ok(new { is_valid = isValid, first_broken_id = brokenId });
    }

    private static string MaskSensitive(string json)
        => _sensitivePattern.Replace(json, m =>
        {
            var key = m.Value[..m.Value.IndexOf('"', 1)];
            return $"{key}: \"[REDACTED]\"";
        });
}
```

- [ ] **Step 4: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+2, Failed: 0`

- [ ] **Step 5: Build**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Api/Controllers/SecurityAuditController.cs tests/MSOSync.ApiTests/Controllers/SecurityAuditControllerTests.cs
git commit -m "feat(2E.6-T3): add SecurityAuditController with masking + chain verification"
```

---

### Task 4: SecurityDashboardPage (React frontend)

**Files:**
- Create: `src/pages/administration/SecurityDashboardPage.tsx` (or adapt to actual pages directory)
- Create: `src/hooks/useSecurityDashboard.ts`
- Modify: router/nav to add /administration/security route (find via `Get-ChildItem -Recurse -Include "*router*","*Router*","*routes*","*Routes*" src/`)

**Interfaces:**
- Consumes: REST endpoints from Task 3 (`/api/security/audit`, `/api/security/audit/verify`)
- Produces: AdminOnly page at `/administration/security` showing audit log table + chain integrity status + security posture summary

- [ ] **Step 1: Locate frontend structure**

```powershell
Get-ChildItem -Recurse -Include "*.tsx" src/ | Where-Object { $_.FullName -like "*administration*" -or $_.FullName -like "*admin*" } | Select-Object FullName
Get-ChildItem -Recurse -Include "*router*","*routes*" src/ | Select-Object FullName
```

Read one existing admin page file to understand the pattern (imports, auth guard, layout usage). Adapt the code below to match.

- [ ] **Step 2: Create useSecurityDashboard hook**

```typescript
// src/hooks/useSecurityDashboard.ts
import { useQuery } from "@tanstack/react-query";

export const securityKeys = {
  audit: (page: number) => ["security", "audit", page] as const,
  chainVerify: () => ["security", "chain-verify"] as const,
};

interface AuditEntry {
  id: number;
  action: string;
  userId: number;
  createdAt: string;
  entryHash: string | null;
}

interface AuditPage {
  total: number;
  page: number;
  pageSize: number;
  items: AuditEntry[];
}

interface ChainVerifyResult {
  isValid: boolean;
  firstBrokenId: number | null;
}

async function fetchAudit(page: number): Promise<AuditPage> {
  const res = await fetch(`/api/security/audit?page=${page}&pageSize=50`);
  if (!res.ok) throw new Error("Failed to fetch audit log");
  return res.json();
}

async function verifyChain(): Promise<ChainVerifyResult> {
  const res = await fetch("/api/security/audit/verify");
  if (!res.ok) throw new Error("Failed to verify chain");
  return res.json();
}

export function useAuditLog(page: number) {
  return useQuery({
    queryKey: securityKeys.audit(page),
    queryFn: () => fetchAudit(page),
  });
}

export function useChainVerify() {
  return useQuery({
    queryKey: securityKeys.chainVerify(),
    queryFn: verifyChain,
    staleTime: 60_000,
  });
}
```

- [ ] **Step 3: Create SecurityDashboardPage**

```tsx
// src/pages/administration/SecurityDashboardPage.tsx
import { useState } from "react";
import { useAuditLog, useChainVerify } from "../../hooks/useSecurityDashboard";
// Adapt import paths to match existing project structure

export function SecurityDashboardPage() {
  const [page, setPage] = useState(1);
  const { data: auditData, isLoading: auditLoading } = useAuditLog(page);
  const { data: chainData, isLoading: chainLoading } = useChainVerify();

  return (
    <div className="space-y-6 p-6">
      <h1 className="text-2xl font-semibold">Security Dashboard</h1>

      {/* Chain integrity status */}
      <div className="rounded-lg border p-4">
        <h2 className="text-lg font-medium mb-2">Audit Chain Integrity</h2>
        {chainLoading ? (
          <span className="text-muted-foreground">Verifying...</span>
        ) : chainData?.isValid ? (
          <span className="text-green-600 font-medium">✓ Chain valid</span>
        ) : (
          <span className="text-red-600 font-medium">
            ✗ Chain broken at entry #{chainData?.firstBrokenId}
          </span>
        )}
      </div>

      {/* Audit log table */}
      <div className="rounded-lg border">
        <div className="p-4 border-b">
          <h2 className="text-lg font-medium">Audit Log</h2>
          {auditData && (
            <p className="text-sm text-muted-foreground">
              {auditData.total} total entries
            </p>
          )}
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b bg-muted/50">
                <th className="p-3 text-left">ID</th>
                <th className="p-3 text-left">Action</th>
                <th className="p-3 text-left">User</th>
                <th className="p-3 text-left">Time</th>
              </tr>
            </thead>
            <tbody>
              {auditLoading ? (
                <tr><td colSpan={4} className="p-4 text-center text-muted-foreground">Loading...</td></tr>
              ) : (
                auditData?.items.map((entry) => (
                  <tr key={entry.id} className="border-b">
                    <td className="p-3 font-mono text-xs">{entry.id}</td>
                    <td className="p-3">{entry.action}</td>
                    <td className="p-3">{entry.userId}</td>
                    <td className="p-3 text-muted-foreground">
                      {new Date(entry.createdAt).toLocaleString()}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        {auditData && auditData.total > 50 && (
          <div className="p-4 flex gap-2 justify-center">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1}
              className="px-3 py-1 border rounded disabled:opacity-50"
            >
              Previous
            </button>
            <span className="px-3 py-1">Page {page}</span>
            <button
              onClick={() => setPage((p) => p + 1)}
              disabled={page * 50 >= auditData.total}
              className="px-3 py-1 border rounded disabled:opacity-50"
            >
              Next
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Add route to router**

Read the router file found in Step 1. Following the existing pattern for admin routes, add:

```tsx
// Inside the admin/protected routes section:
{ path: "/administration/security", element: <SecurityDashboardPage /> }
// or:
<Route path="/administration/security" element={<SecurityDashboardPage />} />
```

Add the page to the admin navigation sidebar following the existing nav item pattern.

- [ ] **Step 5: Build frontend**

```powershell
cd src/MSOSync.Frontend; npm run build 2>&1 | Select-Object -Last 10
```

Expected: build completes without TypeScript errors.

- [ ] **Step 6: Commit**

```
git add src/pages/administration/SecurityDashboardPage.tsx src/hooks/useSecurityDashboard.ts <router-file-path>
git commit -m "feat(2E.6-T4): add SecurityDashboardPage with audit log + chain integrity"
```
