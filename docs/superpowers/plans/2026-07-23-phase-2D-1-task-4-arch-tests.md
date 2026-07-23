# Task 4: Architecture Tests + appsettings Integration

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add architecture tests to enforce that `MSOSync.Metadata` and `MSOSync.Api` never reference `StackExchange.Redis` directly. Add `Cache` section to `appsettings.json`. Run the full test suite and verify the end-to-end setup.

**Prerequisite:** Tasks 1, 2, and 3 complete.

**Files:**
- Modify: `tests/MSOSync.ArchTests/DependencyTests.cs`
- Modify: `src/MSOSync.App/appsettings.json` (or `appsettings.Development.json`)

**Interfaces:**
- Consumes: `ICacheService`, all implementations from Tasks 1–3.
- Produces: Verified constraint that `StackExchange.Redis` is isolated to `MSOSync.Common`.

---

## Steps

- [ ] **Step 1: Write the failing architecture tests**

Open `tests/MSOSync.ArchTests/DependencyTests.cs`. The current file has two tests. Add two more inside the same class.

First, add the assembly loading preamble (find where the existing tests load assemblies and ensure `MSOSync.Metadata` and `MSOSync.Api` assemblies are loaded). The file already uses the pattern of loading assemblies from the output directory. Add the new tests after the existing ones:

```csharp
[Fact]
public void Metadata_MustNotDirectlyReferenceStackExchangeRedis()
{
    var outputDir = Path.GetDirectoryName(typeof(DependencyTests).Assembly.Location)!;
    var path = Path.Combine(outputDir, "MSOSync.Metadata.dll");
    if (File.Exists(path)) Assembly.LoadFrom(path);

    var result = Types.InNamespace("MSOSync.Metadata")
        .ShouldNot()
        .HaveDependencyOn("StackExchange.Redis")
        .GetResult();

    Assert.True(result.IsSuccessful,
        "MSOSync.Metadata must not reference StackExchange.Redis directly. " +
        "All Redis code must go through ICacheService in MSOSync.Common. " +
        "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []));
}

[Fact]
public void Api_MustNotDirectlyReferenceStackExchangeRedis()
{
    var outputDir = Path.GetDirectoryName(typeof(DependencyTests).Assembly.Location)!;
    var path = Path.Combine(outputDir, "MSOSync.Api.dll");
    if (File.Exists(path)) Assembly.LoadFrom(path);

    var result = Types.InNamespace("MSOSync.Api")
        .ShouldNot()
        .HaveDependencyOn("StackExchange.Redis")
        .GetResult();

    Assert.True(result.IsSuccessful,
        "MSOSync.Api must not reference StackExchange.Redis directly. " +
        "All Redis code must go through ICacheService in MSOSync.Common. " +
        "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []));
}
```

- [ ] **Step 2: Run the arch tests to confirm they pass (they should)**

```bash
dotnet test tests/MSOSync.ArchTests/MSOSync.ArchTests.csproj -v normal
```

Expected: All 4 tests pass. If the new tests fail it means `StackExchange.Redis` types leaked into `MSOSync.Metadata` or `MSOSync.Api` — fix the leaking file before continuing.

- [ ] **Step 3: Add `Cache` section to `appsettings.json`**

Find the application's `appsettings.json`. Locate it with:

```bash
find src/MSOSync.App -name "appsettings*.json" 2>/dev/null
```

Open the primary `appsettings.json` and add the `Cache` section (Memory provider — the safe default):

```json
{
  "Cache": {
    "Provider": "Memory",
    "DefaultExpiry": "00:05:00"
  }
}
```

Add it at the same level as existing top-level keys (e.g., `Logging`, `ConnectionStrings`, `Node`).

If there is an `appsettings.Development.json`, you may leave it as-is (it inherits the `Memory` default).

To document the Redis option, add a commented-out example to `appsettings.json` or a developer readme — but do not commit secrets. The connection string must come from environment variables in production:

```json
{
  "Cache": {
    "Provider": "Memory",
    "DefaultExpiry": "00:05:00",
    "RedisConnectionString": null
  }
}
```

- [ ] **Step 4: Verify solution builds cleanly**

```bash
dotnet build MSOSync.sln --no-incremental
```

Expected: Zero errors.

- [ ] **Step 5: Run all test projects**

```bash
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj -v minimal
dotnet test tests/MSOSync.ArchTests/MSOSync.ArchTests.csproj -v minimal
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj --filter "Category!=Integration" -v minimal
```

Expected: All pass. Integration tests with `Category=Integration` are skipped if Docker unavailable.

- [ ] **Step 6: Verify `OverviewSnapshotCache` callers compile**

Check all callers of `OverviewSnapshotCache.Invalidate()` (now renamed to `InvalidateAsync`):

```bash
grep -rn "SnapshotCache\|\.Invalidate\b" src/ --include="*.cs"
```

If any caller still uses the synchronous `Invalidate()`, update it to `await snapshotCache.InvalidateAsync(ct)`. The `OverviewQueryService` and any MediatR handlers are the expected callers.

- [ ] **Step 7: Smoke test with Memory provider (no Redis)**

Run the integration tests that exercise the overview and topology endpoints — these will now use the `ICacheService` via `InMemoryCacheService`:

```bash
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj --filter "FullyQualifiedName~Overview|FullyQualifiedName~Topology|FullyQualifiedName~Metrics" -v normal
```

Expected: All pass.

- [ ] **Step 8: Commit**

```bash
git add tests/MSOSync.ArchTests/DependencyTests.cs
git add src/MSOSync.App/appsettings.json
git commit -m "feat(2D.1-T4): arch tests enforce Redis isolation + appsettings Cache section"
```

---

## Final Verification Checklist

After all four tasks are committed, run this full sweep:

```bash
# Build entire solution
dotnet build MSOSync.sln

# Unit tests
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj
dotnet test tests/MSOSync.ArchTests/MSOSync.ArchTests.csproj

# Integration tests (non-Docker)
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj --filter "Category!=Integration"

# Verify no remaining IMemoryCache usage in Tier A files
grep -rn "IMemoryCache" src/MSOSync.Metadata/Overview/ src/MSOSync.Metadata/Topology/ src/MSOSync.Metadata/Metrics/ src/MSOSync.Metadata/Permissions/ src/MSOSync.Metadata/Services/ --include="*.cs"
# Expected: zero results

# Verify RoutingService still uses IMemoryCache (Tier B — intentional)
grep -n "IMemoryCache" src/MSOSync.Routing/ --include="*.cs" -r
# Expected: RoutingService.cs lines present
```
