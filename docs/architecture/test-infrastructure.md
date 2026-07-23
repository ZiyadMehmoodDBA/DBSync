# Test Infrastructure

## Test Projects (14)

| Project | Type | What It Tests | Test Files | Tests |
|---|---|---|---|---|
| `MSOSync.AppTests` | Unit | App workers, worker registry, health checks, audit timeline, SignalR publishers, options binding, replay worker registry | 11 | 69 |
| `MSOSync.ArchTests` | Architecture | Dependency rules (NetArchTest) | 1 | 2 |
| `MSOSync.ConfigurationTests` | Unit | Configuration templates, assignments, drift, rollouts | 8 | 49 |
| `MSOSync.EngineTests` | Unit | SQL apply, batch state machine, retry, routing | 12 | 82 |
| `MSOSync.IntegrationTests` | Integration | Full API + DB (Testcontainers / WebApplicationFactory) | 61 | ~416 |
| `MSOSync.MetadataTests` | Unit | Domain services, query services, DTOs, validators | 61 | ~551 |
| `MSOSync.PluginTests` | Unit | Plugin loading, lifecycle, registry | 11 | 96 |
| `MSOSync.Plugin.IntegrationTests` | Integration | Plugin full lifecycle (Testcontainers) | 4 | 10 |
| `MSOSync.SchedulerTests` | Unit | SyncJob, PullJob, RetryJob, PurgeJob, RollingOperationWorker, ReplayWorker tick behavior | 6 | 30 |
| `MSOSync.SdkTests` | Unit | Plugin SDK public API surface | 3 | 9 |
| `MSOSync.SecurityTests` | Unit | Auth, JWT, BCrypt, users, audit | 10 | 56 |
| `MSOSync.Tests` | Unit | Tenancy filters, hybrid lookup, tenant id population | 4 | 11 |
| `MSOSync.TransportTests` | Unit | Push/pull clients, compression, node HTTP client | 5 | 23 |
| `MSOSync.TestPlugin` | Helper | Sample plugin assembly consumed by plugin tests (no tests) | 0 | — |

Counts as of Phase 2B.4 (2026-07-22). New since 2B.3: `ClusterHealthTrendServiceTests`
(~8 tests), `RecoveryDashboardQueryServiceTests` (~6 tests), `ClusterDiagnosticsQueryServiceTests`
(~6 tests), `ClusterHealthTrendsApiTests` (4 tests, integration), `RecoveryDashboardApiTests`
(4 tests, integration), `ClusterDiagnosticsApiTests` (4 tests, integration).
Frontend: 14 new Vitest tests across 3 component test files.
Full-solution exit-gate run: all unit assemblies green; `MSOSync.IntegrationTests` environmental
failures (2A-014 + 2A-023) remain accepted.

## Running Tests

```powershell
# All tests
dotnet test D:\MSOSync\MSOSync.sln

# Single project
dotnet test D:\MSOSync\tests\MSOSync.SchedulerTests\MSOSync.SchedulerTests.csproj

# With coverage (Cobertura, via coverlet.collector)
dotnet test D:\MSOSync\MSOSync.sln --collect:"XPlat Code Coverage" --results-directory D:\MSOSync\coverage
```

Always check the per-assembly `Passed!` / `Failed!` summary lines — piping
through filters can mask the exit code.

## Code Coverage

`coverlet.collector` is applied to every test project via
`tests/Directory.Build.props` (unconditional `PackageReference`; version pinned
in `Directory.Packages.props` under Central Package Management). The reference
is *not* conditioned on `$(IsTestProject)` in the root props because the Test
SDK sets that property after `Directory.Build.props` evaluates.

Baseline collected 2026-07-21 into `coverage-baseline/` (git-ignored).
No coverage thresholds are enforced yet; threshold gates are a Phase 2B item.

## Known Environmental Failures

`dotnet test` on a machine without Docker / without the local SQL test login
fails a fixed set of integration tests. These are recorded and accepted in
`docs/architecture/audit-backlog-2A.md`:

- **2A-014** — Testcontainers fixtures (Transport, Engine, Metadata, migration
  smoke, Plugin integration) fail in ~1 ms when Docker is unavailable.
- **2A-023** — `OperationsIntegrationTests` (4 tests) require the local
  `MSOSyncOperations_Test` database login.

All other failures are real regressions and must be investigated.

## Test Conventions

- **Unit test naming:** `{ClassName}Tests.cs` in a matching namespace.
- **Method naming:** `{Method}_{Condition}_{ExpectedResult}` — e.g., `RunTick_skips_engine_when_lock_not_acquired`.
- **RULE-TEST-1:** Never start a `BackgroundService` (`StartAsync`) in unit tests. Test the tick method directly — Scheduler jobs expose `internal` tick methods via `InternalsVisibleTo("MSOSync.SchedulerTests")`.
- **RULE-TEST-2:** Unit tests mock all external dependencies (no real DB, no HTTP). `AppDbContext` may use the EF InMemory provider when a sealed collaborator requires a context instance.
- **RULE-TEST-3:** Integration tests use `Testcontainers.MsSql`; new integration tests belong in an existing integration test project.
- **Assertions:** FluentAssertions. **Mocking:** Moq.
- Scheduler jobs resolve scoped services via `IServiceScopeFactory`; tests build a real `ServiceCollection` with mocks registered (see `MSOSync.SchedulerTests` for the pattern). Sealed collaborators (`SyncEngine`, `RetryProcessor`, `BatchPurger`, `PullClient`) are registered as real instances with mocked constructor interfaces.
- `IDatabaseLockProvider.TryAcquireAsync` returns `IAsyncDisposable?` so tests can fake the lease with `Mock.Of<IAsyncDisposable>()`.
- Tenant query filters are always part of the EF model and read
  `AppDbContext.CurrentTenantId` per instance (2A-015) — test fixtures may
  freely mix contexts with and without an `ICurrentTenantAccessor`.

## Critical Path Coverage

| Area | Coverage | Notes |
|---|---|---|
| Auth / JWT / BCrypt | Unit (`MSOSync.SecurityTests`) | |
| Node lifecycle state machine | Unit (`MSOSync.MetadataTests`) + Integration | |
| Batch transport + apply | Unit (`MSOSync.EngineTests`, `MSOSync.TransportTests`) + Integration | |
| Configuration assignments / rollouts | Unit (`MSOSync.ConfigurationTests`) | |
| Scheduler jobs (Sync/Pull/Retry/Purge) | Unit (`MSOSync.SchedulerTests`) | Added 2A.10 |
| Export job worker | Integration only | No dedicated unit tests — future gap item |
| Plugin loading | Unit + Integration | |
| Tenancy isolation | Unit (`MSOSync.Tests`) + Integration (MultiTenancy suite) | |
| Architecture invariants | `MSOSync.ArchTests` (NetArchTest) | |
| Advanced ops analytics | Unit (`MSOSync.MetadataTests`) + Integration | Cluster summary, config diff, audit multi-filter, operation timeline |
| Cluster analytics (health trends, recovery, diagnostics) | Unit (`MSOSync.MetadataTests`) + Integration | Bucket aggregation, RTO computation, stale lock detection |

## Adding New Tests

1. New services in `MSOSync.Metadata` → `MSOSync.MetadataTests`.
2. New API endpoints → `MSOSync.IntegrationTests`.
3. New scheduler jobs → `MSOSync.SchedulerTests`.
4. New App workers → `MSOSync.AppTests`.
5. Never add test files to source projects.
