# Epic 11E: User Preferences & Saved Workspaces — Design

**Status:** CTO-approved 2026-07-02  
**Guiding principle:** Anything an operator can reasonably configure, customize, or control should be available through the frontend.

---

## Goal

Persist per-user application preferences server-side so filter state, column layouts, sort order, page size, and UI settings survive browser refresh, device switches, and re-login. No localStorage-only hacks as the primary mechanism.

---

## Architecture

### Backend

New `sync_user_preference` table stores arbitrary JSON blobs keyed by `(user_id, preference_key)`. A new `IUserPreferencesService` in `MSOSync.Metadata` handles get-all, upsert, bulk-upsert, and delete. `PreferencesController` exposes four endpoints under `/api/v1/preferences`. Auth: `"ViewerOrAbove"` on all.

Current user identity resolved by username via `ICurrentUserService.GetCurrentUsername()` — no JWT change needed.

### Frontend

`usePreferences()` hook backed by TanStack Query loads all preferences once per session (`staleTime: Infinity`). `AppLayout` calls this hook to prefetch on app boot. Individual pages call `usePreference<T>(key, default)` to read a typed value, and `useSetPreference()` mutation to persist changes optimistically. Theme migrated from localStorage-only to backend-synced (localStorage retained as instant-paint fallback on initial render before hydration).

---

## Preference Key Namespace

String constants in `PreferenceKeys` (as-const object, no enum):

```
page.events.filter            EventFilter JSON
page.events.pageSize          number
page.events.sort              { field: string; direction: 'asc'|'desc' }
page.events.columns           string[] (visible column IDs in order)

page.incoming-batches.filter
page.incoming-batches.pageSize
page.incoming-batches.sort
page.incoming-batches.columns

page.outgoing-batches.filter
page.outgoing-batches.pageSize
page.outgoing-batches.sort
page.outgoing-batches.columns

page.audit.filter
page.audit.pageSize
page.audit.sort
page.audit.columns

page.nodes.columns
page.nodes.pageSize

page.users.columns
page.users.pageSize

page.parameters.columns

ui.theme                      'light' | 'dark'
ui.defaultLandingPage         route string e.g. '/dashboard'
ui.autoRefresh.enabled        boolean
ui.autoRefresh.intervalSeconds number (10–300)
ui.notifications.enabled      boolean
```

---

## Database Schema

Migration M017. Table `msosync.sync_user_preference`:

| Column | Type | Constraints |
|--------|------|-------------|
| preference_id | bigint IDENTITY | PK |
| user_id | bigint | FK → sync_user(user_id) ON DELETE CASCADE |
| preference_key | nvarchar(100) | NOT NULL |
| preference_value | nvarchar(max) | NOT NULL (JSON text) |
| updated_at | datetime2(7) | NOT NULL, DEFAULT SYSUTCDATETIME() |

Unique constraint: `(user_id, preference_key)`.

---

## API Contract

All endpoints: `[Authorize(Policy = "ViewerOrAbove")]`. Current user from JWT.

```
GET    /api/v1/preferences           → 200 { "page.events.pageSize": 25, "ui.theme": "dark", ... }
PUT    /api/v1/preferences/{key}     → 200  body: any JSON value
PUT    /api/v1/preferences           → 200  body: { key: value, ... }
DELETE /api/v1/preferences/{key}     → 204
```

- `key` max length 100 chars; PUT single validates and returns 400 if invalid
- GET returns `{}` (empty object) if no preferences saved yet
- DELETE is idempotent — returns 204 even if key did not exist

---

## Frontend Hook Contract

```typescript
// Fetches all prefs once; staleTime: Infinity
usePreferences(): UseQueryResult<Record<string, unknown>>

// Reads one typed key; returns defaultValue if prefs not loaded or key absent
usePreference<T>(key: string, defaultValue: T): T

// Optimistic single-key write
useSetPreference(): UseMutationResult<..., { key: string; value: unknown }>

// Optimistic single-key delete
useDeletePreference(): UseMutationResult<..., string>
```

### Page integration pattern

```tsx
// In any page component:
const savedFilter = usePreference<EventFilter>(PreferenceKeys.eventsFilter, DEFAULT_FILTER);
const { mutate: setPref } = useSetPreference();

// Initialize state once when prefs resolve
const [filter, setFilter] = useState<EventFilter>(DEFAULT_FILTER);
const prefsApplied = useRef(false);
useEffect(() => {
  if (!prefsApplied.current && savedFilter !== DEFAULT_FILTER) {
    setFilter(savedFilter);
    prefsApplied.current = true;
  }
}, [savedFilter]);

// Persist on change
function handleFilterChange(next: EventFilter) {
  setFilter(next);
  setPref({ key: PreferenceKeys.eventsFilter, value: next });
}
```

---

## Deferred Scope (not in 11E)

- Named saved workspaces (filter presets with user-defined names)
- Dashboard card order / arrangement
- Topology graph zoom/pan/selected-node state
- Column widths (beyond visibility/order)
- Preference export/import

---

## Pages Integrated in 11E

| Page | Keys persisted |
|------|---------------|
| EventsPage | filter, pageSize, sort, columns |
| IncomingBatchesPage | filter, pageSize, sort, columns |
| OutgoingBatchesPage | filter, pageSize, sort, columns |
| AuditPage | filter, pageSize, sort, columns |
| NodesPage | columns, pageSize |
| UsersPage | columns, pageSize |
| ParametersPage | columns |
| AppLayout | theme (migrate from localStorage) |
| ProfilePage (new Settings section) | defaultLandingPage, autoRefresh, notifications |

---

## Global Constraints

- C# 13, .NET 9, `TreatWarningsAsErrors = true`
- EF Core 9 — `AsNoTracking()` on reads; explicit `SaveChangesAsync(ct)` on writes
- No new NuGet packages
- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword
- All frontend imports relative (no `@/` aliases)
- No new npm packages
- Auth policy `"ViewerOrAbove"` on all new endpoints
- Preference value stored as raw JSON text (nvarchar(max))
- Unit tests: `TestDbContext.Create()` (SQLite in-memory)
