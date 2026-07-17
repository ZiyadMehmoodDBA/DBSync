# Phase 2A.10 — Test Infrastructure

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Configure code coverage collection (coverlet), run a baseline coverage report, identify critical-path unit test gaps in the Scheduler layer (SyncJob, PullJob, RetryJob, PurgeJob have no dedicated unit tests), and add minimal gap-filler tests. Commit coverage config and new tests.

**Architecture:** 13 test projects, 269 test files. Heavy integration test coverage (Testcontainers.MsSql). No code coverage tool configured — no coverlet, no dotnet-coverage, no .runsettings. Four scheduler jobs (SyncJob, PullJob, RetryJob, PurgeJob) lack dedicated unit tests — only integration tests exist for these. The unit tests that do exist live in `MSOSync.AppTests` and `MSOSync.MetadataTests`. All test projects use xunit + FluentAssertions + Moq.

**Tech Stack:** C# 13 / .NET 9 / xunit / FluentAssertions / Moq / coverlet.collector

## Global Constraints

- No new product features. Scope is test infrastructure and gap coverage.
- Definition of Complete: coverlet configured + baseline report generated + new Scheduler unit tests committed + `dotnet test` exits 0.
- RULE-TEST-1: Unit tests must not start `BackgroundService.StartAsync` — test the tick method directly.
- RULE-TEST-2: Unit tests must mock all external dependencies (no DB, no HTTP).
- RULE-TEST-3: Integration tests use `Testcontainers.MsSql` — do not add more without an integration test project.
- Do not remove or alter existing tests.

---

## File Map

**Modify:**
- `Directory.Build.props` — add coverlet collector package reference

**Create:**
- `tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj`
- `tests/MSOSync.SchedulerTests/SyncJobTests.cs`
- `tests/MSOSync.SchedulerTests/PullJobTests.cs`
- `tests/MSOSync.SchedulerTests/RetryJobTests.cs`
- `tests/MSOSync.SchedulerTests/PurgeJobTests.cs`
- `docs/architecture/test-infrastructure.md`

---

## Task 1: Configure Code Coverage

**Files:**
- Modify: `Directory.Build.props`

- [ ] **Step 1: Read Directory.Build.props**

```
cat D:\MSOSync\Directory.Build.props
```

- [ ] **Step 2: Add coverlet collector to all test projects**

Add `coverlet.collector` to `Directory.Build.props` inside the `<Project>` element. Check if there's already a `<ItemGroup>` for test-specific packages (look for `IsTestProject` condition). If not, add:

```xml
<ItemGroup Condition="'$(IsTestProject)' == 'true'">
  <PackageReference Include="coverlet.collector" Version="6.0.2">
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
</ItemGroup>
```

If `Directory.Build.props` already has test project conditions, add the `coverlet.collector` reference there. Do not duplicate existing conditions.

- [ ] **Step 3: Add IsTestProject property to each test project**

Check if test project files already have `<IsTestProject>true</IsTestProject>`. Run:

```powershell
grep -rn "IsTestProject" D:\MSOSync\tests\ --include="*.csproj"
```

If no matches, the test projects need this property. But first check if the existing projects already reference coverlet directly:

```powershell
grep -rn "coverlet" D:\MSOSync\tests\ --include="*.csproj"
```

If coverlet is already in every test project, skip this step. If not, add `<IsTestProject>true</IsTestProject>` to each `.csproj` that doesn't have it, or add coverlet directly to one representative project first and verify it works.

- [ ] **Step 4: Build to verify no errors**

```powershell
dotnet build D:\MSOSync\MSOSync.sln -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors.

- [ ] **Step 5: Run coverage baseline for unit test projects only**

```powershell
dotnet test D:\MSOSync\MSOSync.sln --collect:"XPlat Code Coverage" --results-directory D:\MSOSync\coverage-baseline --filter "Category!=Integration" -v n
```

This generates `.cobertura.xml` files in `D:\MSOSync\coverage-baseline`. Note which projects ran and what the overall pass/fail count is. The report is used to establish a baseline — we do not set thresholds in this phase.

- [ ] **Step 6: Commit**

```
git add Directory.Build.props
git commit -m "chore(2A.10): add coverlet.collector to test projects via Directory.Build.props"
```

---

## Task 2: Create Scheduler Unit Test Project

**Files:**
- Create: `tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj`

- [ ] **Step 1: Read an existing test project file for reference**

```
cat D:\MSOSync\tests\MSOSync.AppTests\MSOSync.AppTests.csproj
```

Note the SDK, TargetFramework, package versions, and project references used. Mirror these in the new project.

- [ ] **Step 2: Create the project file**

Create `tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
    <PackageReference Include="Moq" Version="4.20.72" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Scheduler\MSOSync.Scheduler.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Common\MSOSync.Common.csproj" />
  </ItemGroup>

</Project>
```

**Note:** Match exact package versions to those in `MSOSync.AppTests.csproj` — do not guess. Update the versions above to match what you see in the reference project file from Step 1.

- [ ] **Step 3: Add project to solution**

```powershell
dotnet sln D:\MSOSync\MSOSync.sln add D:\MSOSync\tests\MSOSync.SchedulerTests\MSOSync.SchedulerTests.csproj
```

- [ ] **Step 4: Build**

```powershell
dotnet build D:\MSOSync\tests\MSOSync.SchedulerTests\MSOSync.SchedulerTests.csproj -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors (empty project, no tests yet).

---

## Task 3: SyncJob Unit Tests

**Files:**
- Create: `tests/MSOSync.SchedulerTests/SyncJobTests.cs`

**Context:** `SyncJob` is a `BackgroundService` in `src/MSOSync.Scheduler/SyncJob.cs`. It acquires a distributed lock (`IDatabaseLockProvider`), reads a `NodeProperties` option, reads pending triggers (`IEventReader`), and applies them via `IApplyService`. After 2A.8 it reads interval from `IOptions<SyncOptions>`. After 2A.9 it calls `IWorkerStatusRegistry`. Test the tick behavior directly — do not call `StartAsync`.

- [ ] **Step 1: Read SyncJob source**

```
cat D:\MSOSync\src\MSOSync.Scheduler\SyncJob.cs
```

Note: the exact class name of the tick method, what it does when the lock is not acquired, and the constructor parameters.

- [ ] **Step 2: Write SyncJobTests.cs**

Create `tests/MSOSync.SchedulerTests/SyncJobTests.cs`. The exact test code depends on what you read in SyncJob.cs. Use the pattern below as a template — adapt method names, parameter names, and mock setup to match the real implementation:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence.Lock;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SyncJobTests
{
    private readonly Mock<IDatabaseLockProvider>    _lockProvider    = new();
    private readonly Mock<IWorkerStatusRegistry>    _registry        = new();
    private readonly Mock<IOptions<NodeProperties>> _nodeProps       = new();

    public SyncJobTests()
    {
        _nodeProps.Setup(x => x.Value).Returns(new NodeProperties { NodeId = "test-node" });
    }

    // NOTE: After reading SyncJob.cs, fill in the correct constructor params and tick method name.
    // The test below is a template — adapt to the real implementation.

    [Fact]
    public async Task Tick_skips_when_lock_not_acquired()
    {
        // Arrange
        _lockProvider
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Build SyncJob with all mocked dependencies
        // var sut = new SyncJob(_lockProvider.Object, _registry.Object, _nodeProps.Object, NullLogger<SyncJob>.Instance, ...);

        // Act
        // await sut.ExecuteTickAsync(CancellationToken.None);

        // Assert
        // _applyService.Verify(x => x.ApplyAsync(It.IsAny<...>()), Times.Never);
        throw new NotImplementedException("Fill in after reading SyncJob.cs constructor");
    }
}
```

**IMPORTANT:** The placeholder `throw new NotImplementedException` above is only a scaffold. After reading `SyncJob.cs` in Step 1, write real tests that cover:
1. Lock not acquired → no apply service calls.
2. Lock acquired, no pending events → apply service called with empty list (or not called if it guards for empty).
3. Lock acquired, events present → apply service called with events.

Use `Moq` for all external dependencies. Do not start the BackgroundService — call the tick method directly or use reflection if it's private.

- [ ] **Step 3: Run SyncJob tests**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.SchedulerTests\MSOSync.SchedulerTests.csproj --filter "SyncJobTests" -v n
```

Expected: All tests PASS.

---

## Task 4: PullJob Unit Tests

**Files:**
- Create: `tests/MSOSync.SchedulerTests/PullJobTests.cs`

**Context:** `PullJob` pulls batches from child nodes via `INodeHttpClient`, writes them via `IBatchStateMachine`, and applies via `IApplyService`. It skips when node is in Push mode. After 2A.8, reads interval from `IOptions<SyncOptions>`. After 2A.9, calls `IWorkerStatusRegistry`.

- [ ] **Step 1: Read PullJob source**

```
cat D:\MSOSync\src\MSOSync.Scheduler\PullJob.cs
```

Note constructor parameters and tick method. Identify the push-mode guard condition.

- [ ] **Step 2: Write PullJobTests.cs**

Create `tests/MSOSync.SchedulerTests/PullJobTests.cs`. Cover:
1. Node is in Push mode → no HTTP calls made.
2. Node is in Pull mode, no child nodes → loop runs 0 times.
3. Node is in Pull mode, HTTP call returns batches → batches written via `IBatchStateMachine`.

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class PullJobTests
{
    // NOTE: After reading PullJob.cs, declare the correct mocks and constructor params.
    // The pattern is the same as SyncJobTests — mock all deps, call the tick method.

    [Fact]
    public async Task Tick_skips_when_node_in_push_mode()
    {
        // Arrange: INodeSyncPolicy returns Push mode
        // Act: call tick
        // Assert: INodeHttpClient never called
        throw new NotImplementedException("Fill in after reading PullJob.cs");
    }

    [Fact]
    public async Task Tick_pulls_batches_from_child_nodes()
    {
        // Arrange: Pull mode, one child, INodeHttpClient returns one batch
        // Act: call tick
        // Assert: IBatchStateMachine.TransitionAsync called once
        throw new NotImplementedException("Fill in after reading PullJob.cs");
    }
}
```

Replace `throw new NotImplementedException` with real test code after reading the source.

- [ ] **Step 3: Run PullJob tests**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.SchedulerTests\MSOSync.SchedulerTests.csproj --filter "PullJobTests" -v n
```

Expected: All tests PASS.

---

## Task 5: RetryJob and PurgeJob Unit Tests

**Files:**
- Create: `tests/MSOSync.SchedulerTests/RetryJobTests.cs`
- Create: `tests/MSOSync.SchedulerTests/PurgeJobTests.cs`

- [ ] **Step 1: Read RetryJob and PurgeJob source**

```
cat D:\MSOSync\src\MSOSync.Scheduler\RetryJob.cs
cat D:\MSOSync\src\MSOSync.Scheduler\PurgeJob.cs
```

- [ ] **Step 2: Write RetryJobTests.cs**

`RetryJob` acquires a lock and requeues failed batches. Cover:
1. Lock not acquired → no batch queries.
2. Lock acquired, no failed batches → no state transitions.
3. Lock acquired, failed batches → requeued via `IBatchStateMachine`.

```csharp
namespace MSOSync.SchedulerTests;

public sealed class RetryJobTests
{
    [Fact]
    public async Task Tick_skips_when_lock_not_acquired()
    {
        throw new NotImplementedException("Fill in after reading RetryJob.cs");
    }

    [Fact]
    public async Task Tick_requeues_failed_batches()
    {
        throw new NotImplementedException("Fill in after reading RetryJob.cs");
    }
}
```

- [ ] **Step 3: Write PurgeJobTests.cs**

`PurgeJob` sleeps until 02:00 UTC then purges events and batches. Testing the sleep loop directly is impractical — test the purge execution method. Cover:
1. Purge method calls `IEventPurger.PurgeAsync` with the correct retention cutoff.
2. Purge method calls batch cleanup with correct cutoff.

```csharp
namespace MSOSync.SchedulerTests;

public sealed class PurgeJobTests
{
    [Fact]
    public async Task Purge_deletes_events_older_than_retention_window()
    {
        throw new NotImplementedException("Fill in after reading PurgeJob.cs — test the purge execution, not the sleep loop");
    }
}
```

If `PurgeJob`'s purge logic is inside `ExecuteAsync` in a way that cannot be called in isolation (mixed with the sleep loop), add a private `RunPurgeAsync(CancellationToken)` method extracted from the loop body, or use the `internal` visibility modifier and `[assembly: InternalsVisibleTo("MSOSync.SchedulerTests")]` in `PurgeJob.cs`. Only extract if feasible without restructuring — if it's too entangled, document this as a future refactor and write a minimal smoke test instead.

- [ ] **Step 4: Run all new tests**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.SchedulerTests\MSOSync.SchedulerTests.csproj -v n
```

Expected: All tests PASS.

- [ ] **Step 5: Commit new test project**

```
git add tests/MSOSync.SchedulerTests/
git add MSOSync.sln
git commit -m "test(2A.10): MSOSync.SchedulerTests — SyncJob, PullJob, RetryJob, PurgeJob unit tests"
```

---

## Task 6: Write Test Infrastructure Document

**Files:**
- Create: `docs/architecture/test-infrastructure.md`

- [ ] **Step 1: Create test-infrastructure.md**

Create `docs/architecture/test-infrastructure.md`:

```markdown
# Test Infrastructure

## Test Projects

| Project | Type | What It Tests | Test Files |
|---|---|---|---|
| `MSOSync.AppTests` | Unit + Integration | Workers, health, audit timeline, SignalR publishers | 8 |
| `MSOSync.ArchTests` | Architecture | Dependency rules (ArchUnitNET or NetArchTest) | 1 |
| `MSOSync.ConfigurationTests` | Unit | Configuration assignments, drift, rollouts | 9 |
| `MSOSync.EngineTests` | Unit | SQL apply, batch state machine, retry, routing | 13 |
| `MSOSync.IntegrationTests` | Integration | Full API + DB (Testcontainers) | 60 |
| `MSOSync.MetadataTests` | Unit | Domain services, query services | 42 |
| `MSOSync.PluginTests` | Unit | Plugin loading, lifecycle, registry | 11 |
| `MSOSync.Plugin.IntegrationTests` | Integration | Plugin full lifecycle (Testcontainers) | 5 |
| `MSOSync.SchedulerTests` | Unit | SyncJob, PullJob, RetryJob, PurgeJob tick behavior | 4 |
| `MSOSync.SdkTests` | Unit | Plugin SDK public API surface | 3 |
| `MSOSync.SecurityTests` | Unit | Auth, JWT, BCrypt, tenancy | 10 |
| `MSOSync.Tests` | Unit | Tenancy filters | 4 |
| `MSOSync.TransportTests` | Unit | Batch transport, compression, acknowledgement | 7 |

## Running Tests

```powershell
# All tests
dotnet test D:\MSOSync\MSOSync.sln -v n

# Unit tests only (exclude integration)
dotnet test D:\MSOSync\MSOSync.sln --filter "Category!=Integration" -v n

# With coverage
dotnet test D:\MSOSync\MSOSync.sln --collect:"XPlat Code Coverage" --results-directory D:\MSOSync\coverage --filter "Category!=Integration"
```

## Code Coverage

Coverage is collected via `coverlet.collector` (XPlat Code Coverage, Cobertura format).
Results written to `--results-directory` as `coverage.cobertura.xml` per project.

No coverage thresholds are enforced in CI at this time. Phase 2B will add threshold gates.

## Test Conventions

- **Unit test naming:** `{ClassName}Tests.cs` in a matching namespace.
- **Method naming:** `{Method}_{Condition}_{ExpectedResult}` — e.g., `Tick_skips_when_lock_not_acquired`.
- **No database in unit tests:** Mock `AppDbContext` or use in-memory provider only if unavoidable.
- **Integration tests:** Use `Testcontainers.MsSql`. Always derive from the shared `IntegrationTestBase` fixture.
- **BackgroundService tests:** Never call `StartAsync`. Extract and test the tick/cycle method directly.
- **Assertions:** FluentAssertions exclusively. No raw `Assert.` calls.
- **Mocking:** Moq exclusively.

## Critical Path Coverage

Areas with highest risk if untested:

| Area | Coverage Type | Notes |
|---|---|---|
| Auth / JWT / BCrypt | Unit (`MSOSync.SecurityTests`) | Full coverage |
| Lifecycle state machine | Unit + Integration | Full coverage |
| Batch transport + apply | Unit (`MSOSync.EngineTests`) + Integration | Full coverage |
| Configuration assignments | Unit (`MSOSync.ConfigurationTests`) | Full coverage |
| Scheduler jobs | Unit (`MSOSync.SchedulerTests`) | Added 2A.10 |
| Export job worker | Integration (`MSOSync.IntegrationTests`) | No dedicated unit tests |
| Plugin loading | Unit + Integration | Full coverage |
| Tenancy isolation | Unit + Integration | Full coverage |

## Adding New Tests

1. For new services in `MSOSync.Metadata`: add to `MSOSync.MetadataTests`.
2. For new API endpoints: add integration test to `MSOSync.IntegrationTests`.
3. For new workers in `MSOSync.Scheduler`: add to `MSOSync.SchedulerTests`.
4. For new workers in `MSOSync.App`: add to `MSOSync.AppTests`.
5. Do not add test files to the source projects.
```

- [ ] **Step 2: Run all tests**

```
dotnet test D:\MSOSync\MSOSync.sln -v n
```

Expected: All tests pass (including new SchedulerTests).

- [ ] **Step 3: Commit**

```
git add docs/architecture/test-infrastructure.md
git commit -m "docs(2A.10): test infrastructure reference and coverage baseline"
```

---

## Completion Criteria

2A.10 is **Complete** when:
1. `coverlet.collector` is referenced in all test projects (via `Directory.Build.props` or directly).
2. `dotnet test --collect:"XPlat Code Coverage"` produces `.cobertura.xml` output without error.
3. `MSOSync.SchedulerTests` project exists with unit tests for SyncJob, PullJob, RetryJob, and PurgeJob tick behavior.
4. `dotnet test D:\MSOSync\MSOSync.sln` exits 0 with all tests passing.
5. `docs/architecture/test-infrastructure.md` committed.
