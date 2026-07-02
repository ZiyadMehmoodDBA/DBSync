# Epic 11E: User Preferences & Saved Workspaces Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist per-user filter state, column layouts, sort order, page size, and UI settings server-side so operator preferences survive browser refresh, device switches, and re-login.

**Architecture:** Backend adds `sync_user_preference` table (M017 migration), `IUserPreferencesService`, and `PreferencesController` (4 endpoints). Frontend adds a typed `usePreferences()` TanStack Query hook with optimistic writes; `AppLayout` prefetches on boot; 9 pages/components read and write their relevant preference keys.

**Tech Stack:** C# 13 / .NET 9 / EF Core 9 / React 19 / TanStack Query v5 / TypeScript (erasableSyntaxOnly)

**Spec:** `docs/superpowers/specs/2026-07-02-epic11e-user-preferences-design.md`

## Global Constraints

- C# 13, .NET 9, `TreatWarningsAsErrors = true` — zero warnings
- EF Core 9 — `AsNoTracking()` on all reads; `SaveChangesAsync(ct)` on writes
- No new NuGet packages; no new npm packages
- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword; use `as const`
- All frontend imports relative — no `@/` aliases
- Auth policy `"ViewerOrAbove"` on all new endpoints
- Unit tests: `TestDbContext.Create()` (SQLite in-memory)
- Never `git add -A` or `git add .` — stage files by name
- Never commit `.env` files

---

## File Structure

### New files — backend
```
src/MSOSync.Persistence/Entities/SyncUserPreference.cs
src/MSOSync.Persistence/Configurations/SyncUserPreferenceConfiguration.cs
src/MSOSync.Metadata/Preferences/IUserPreferencesService.cs
src/MSOSync.Metadata/Preferences/UserPreferencesService.cs
src/MSOSync.Api/Controllers/PreferencesController.cs
tests/MSOSync.MetadataTests/Preferences/UserPreferencesServiceTests.cs
```

### Modified files — backend
```
src/MSOSync.Persistence/AppDbContext.cs                  — add UserPreferences DbSet
src/MSOSync.Persistence/Migrations/                      — add M017_UserPreferences (dotnet ef)
src/MSOSync.Metadata/MetadataServiceExtensions.cs        — register IUserPreferencesService
```

### New files — frontend
```
src/MSOSync.Frontend/src/shared/types/preferences.ts     — PreferenceKeys + types
src/MSOSync.Frontend/src/shared/api/preferences.ts       — 4 API functions
src/MSOSync.Frontend/src/shared/hooks/usePreferences.ts  — usePreferences, usePreference, useSetPreference, useDeletePreference
```

### Modified files — frontend
```
src/MSOSync.Frontend/src/shared/types/index.ts           — export preferences types
src/MSOSync.Frontend/src/shared/queryKeys.ts             — add userPreferences key
src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx       — prefetch prefs; sync theme
src/MSOSync.Frontend/src/features/events/EventsPage.tsx
src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx
src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx
src/MSOSync.Frontend/src/features/audit/AuditPage.tsx
src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx
src/MSOSync.Frontend/src/features/users/UsersPage.tsx
src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx
src/MSOSync.Frontend/src/features/profile/ProfilePage.tsx  — add Settings section
```

---

## Tasks

| # | Name | Deliverable |
|---|------|-------------|
| 1 | [Backend entity + service](2026-07-02-epic11e-task-1-backend-entity-service.md) | M017 migration + SyncUserPreference entity + IUserPreferencesService + 7 unit tests |
| 2 | [Backend controller](2026-07-02-epic11e-task-2-backend-controller.md) | PreferencesController (4 endpoints) + DI registration |
| 3 | [Frontend shared infrastructure](2026-07-02-epic11e-task-3-frontend-infrastructure.md) | PreferenceKeys + types + API functions + usePreferences hook family |
| 4 | [Frontend page integrations](2026-07-02-epic11e-task-4-frontend-integrations.md) | 9 pages/components reading & writing preferences + theme migration |
