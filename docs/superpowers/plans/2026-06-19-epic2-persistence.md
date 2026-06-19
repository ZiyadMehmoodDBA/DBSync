# Epic 2: Persistence Foundation — Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `MSOSync.Persistence` completely — 23 entities, Fluent API configurations, 8 EF Core migrations, 7 query objects, DI registration, health check, and 11 integration tests.

**Architecture:** Code-first EF Core 9 targeting SQL Server 2022. No repository abstractions — `AppDbContext` injected directly. Schema name from `MSOSYNC_SCHEMA` env var (default: `msosync`).

**Tech Stack:** C# 13 / .NET 9 / EF Core 9.0.0 / Testcontainers.MsSql 4.4.0 / xUnit 2.9.3 / FluentAssertions 6.12.2

## Global Constraints

- C# 13, .NET 9, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- EF Core 9.0.0 — packages already in `Directory.Packages.props` and `MSOSync.Persistence.csproj`
- No `BaseEntity`, no repository interfaces, no `virtual` navigation properties
- All `DateTime` columns: `.HasColumnType("datetime2(7)")` in Fluent API
- All JSON columns (`nvarchar(max)`): `.HasColumnType("nvarchar(max)")`
- `AsNoTracking()` on every query object query
- Schema always from `Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync"` in configs
- Migrations hardcode `"msosync"` as schema name
- M008 seed inserts use `IF NOT EXISTS` guards (idempotent)
- dotnet PATH — prepend before every `dotnet` / `dotnet-ef` command:
  ```powershell
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```
- All entities are `sealed`, no class hierarchy

---

## Task Files

| # | Plan file | Deliverable |
|---|---|---|
| 1 | [task1-tooling](2026-06-19-epic2-task1-tooling.md) | `AppDbContextFactory.cs` + `dotnet-ef` global tool |
| 2 | [task2-entities](2026-06-19-epic2-task2-entities.md) | 23 entity classes in `Entities/` |
| 3 | [task3-configurations](2026-06-19-epic2-task3-configurations.md) | 23 `IEntityTypeConfiguration<T>` in `Configurations/` |
| 4 | [task4-dbcontext](2026-06-19-epic2-task4-dbcontext.md) | `AppDbContext.cs` with 23 DbSets |
| 5 | [task5-migrations](2026-06-19-epic2-task5-migrations.md) | Model snapshot + migrations M001–M007 |
| 6 | [task6-seed](2026-06-19-epic2-task6-seed.md) | M008 seed migration + `dotnet ef database update` smoke test |
| 7 | [task7-queries-di](2026-06-19-epic2-task7-queries-di.md) | 7 query objects + `PersistenceServiceExtensions` + `PersistenceHealthCheck` |
| 8 | [task8-integration-tests](2026-06-19-epic2-task8-integration-tests.md) | `DatabaseFixture` + 11 integration tests |

---

## Spec Reference

`docs/superpowers/specs/2026-06-18-epic2-persistence-design.md`
