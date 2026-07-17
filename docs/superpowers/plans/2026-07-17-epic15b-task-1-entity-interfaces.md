# Task 1: Add ITenantScoped + Guid TenantId to 21 Entities

**Part of:** [Epic 15B Domain Tenant Migration](2026-07-17-epic15b-domain-tenant-migration.md)

**Goal:** All 21 deferred entities implement `ITenantScoped` and carry `public Guid TenantId { get; set; }`. They already have `[TenantScoped]` attribute — do not remove it.

**Files to modify** (all in `src/MSOSync.Persistence/Entities/`):
- `SyncRegistrationRequest.cs`, `SyncNodeBootstrapToken.cs`, `SyncNodeLifecycleHistory.cs`, `SyncNodeConnectivityHistory.cs`
- `SyncDataEvent.cs`, `SyncDataEventBatch.cs`, `SyncOutgoingBatch.cs`, `SyncIncomingBatch.cs`, `SyncBatchError.cs`
- `SyncConfigurationTemplate.cs`, `SyncConfigurationTemplateVersion.cs`, `SyncNodeConfigurationOverride.cs`, `SyncNodeConfigurationHistory.cs`, `SyncConfigurationRollout.cs`
- `SyncRuntimeStats.cs`, `SyncAudit.cs`, `SyncOperation.cs`, `SyncExportJob.cs`
- `SyncNotification.cs`, `SyncUserNotification.cs`, `SyncUserRefreshToken.cs`

**Interfaces:**
- Consumes: `ITenantScoped` from `MSOSync.Common/Tenancy/ITenantScoped.cs` — `public interface ITenantScoped { Guid TenantId { get; set; } }`
- Produces: 21 entity classes implementing `ITenantScoped`, each with `public Guid TenantId { get; set; }`

---

## Pattern

For EVERY one of the 21 entities below, apply this identical change:

**Find:**
```csharp
[TenantScoped]
public class SyncXxx
```

**Change to:**
```csharp
[TenantScoped]
public class SyncXxx : ITenantScoped
```

Then add at the end of the class body (before the closing `}`):
```csharp
public Guid TenantId { get; set; }
```

The `using MSOSync.Common.Tenancy;` import is needed at the top of each file. Check if it is already present; add it if missing.

---

- [ ] **Step 1: Open each entity file and apply the pattern**

Apply to all 21 files in order:

**Group 1 — Node Management**

`src/MSOSync.Persistence/Entities/SyncRegistrationRequest.cs`
`src/MSOSync.Persistence/Entities/SyncNodeBootstrapToken.cs`
`src/MSOSync.Persistence/Entities/SyncNodeLifecycleHistory.cs`
`src/MSOSync.Persistence/Entities/SyncNodeConnectivityHistory.cs`

**Group 2 — Synchronization Engine**

`src/MSOSync.Persistence/Entities/SyncDataEvent.cs`
`src/MSOSync.Persistence/Entities/SyncDataEventBatch.cs`
`src/MSOSync.Persistence/Entities/SyncOutgoingBatch.cs`
`src/MSOSync.Persistence/Entities/SyncIncomingBatch.cs`
`src/MSOSync.Persistence/Entities/SyncBatchError.cs`

**Group 3 — Configuration Management**

`src/MSOSync.Persistence/Entities/SyncConfigurationTemplate.cs`
`src/MSOSync.Persistence/Entities/SyncConfigurationTemplateVersion.cs`
`src/MSOSync.Persistence/Entities/SyncNodeConfigurationOverride.cs`
`src/MSOSync.Persistence/Entities/SyncNodeConfigurationHistory.cs`
`src/MSOSync.Persistence/Entities/SyncConfigurationRollout.cs`

**Group 4 — Operations & Audit**

`src/MSOSync.Persistence/Entities/SyncRuntimeStats.cs`
`src/MSOSync.Persistence/Entities/SyncAudit.cs`
`src/MSOSync.Persistence/Entities/SyncOperation.cs`
`src/MSOSync.Persistence/Entities/SyncExportJob.cs`

**Group 5 — User & Runtime**

`src/MSOSync.Persistence/Entities/SyncNotification.cs`
`src/MSOSync.Persistence/Entities/SyncUserNotification.cs`
`src/MSOSync.Persistence/Entities/SyncUserRefreshToken.cs`

For each file:
1. Add `using MSOSync.Common.Tenancy;` at the top (if not present)
2. Change `public class SyncXxx` → `public class SyncXxx : ITenantScoped`
3. Add `public Guid TenantId { get; set; }` as the last property in the class

- [ ] **Step 2: Build to verify no compile errors**

```
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj
```

Expected: `Build succeeded. 0 Error(s) 0 Warning(s)`

If you see "CS0535: 'SyncXxx' does not implement interface member 'ITenantScoped.TenantId'", you forgot to add the property to that entity.

- [ ] **Step 3: Run the EntityOwnershipGateTest to confirm no regressions**

```
dotnet test tests/MSOSync.Tests/MSOSync.Tests.csproj --filter "EntityOwnershipGateTests" -v normal
```

Expected: PASS. The gate test checks that every entity in `MSOSync.Persistence.Entities` has exactly one ownership marker (`[GlobalEntity]`, `[HybridEntity]`, or `[TenantScoped]`). Adding `ITenantScoped` alongside `[TenantScoped]` does not break the gate — the gate scans for attributes, not interfaces.

If the gate fails with "does not have exactly one ownership marker", an entity was accidentally given two attributes. Inspect the entity and remove the duplicate.

- [ ] **Step 4: Run full unit test suite to catch any regressions**

```
dotnet test MSOSync.sln --filter "Category!=Integration" -v minimal
```

Expected: all existing tests pass. No new failures.

- [ ] **Step 5: Commit**

```
git add src/MSOSync.Persistence/Entities/
git commit -m "feat(15B-1): ITenantScoped + TenantId on 21 deferred entities"
```
