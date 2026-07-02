# Task 4: Frontend Page Integrations

**Part of:** Epic 11E — User Preferences & Saved Workspaces  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11e-user-preferences-design.md`  
**Depends on:** Task 3 (hooks and PreferenceKeys must exist)

## Files Modified

- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`
- `src/MSOSync.Frontend/src/features/events/EventsPage.tsx`
- `src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx`
- `src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx`
- `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx`
- `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx`
- `src/MSOSync.Frontend/src/features/users/UsersPage.tsx`
- `src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx`
- `src/MSOSync.Frontend/src/features/profile/ProfilePage.tsx`

## Interfaces Consumed (from Task 3)

```typescript
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys, Theme, SortPreference } from '../../shared/types/preferences';
// adjust relative path per file location
```

---

## Global Constraints

- TypeScript `erasableSyntaxOnly = true` — no `enum`
- All imports relative — no `@/`
- No new npm packages
- Read each file before modifying — the current structure matters

---

## Integration Pattern (apply to every server-paged page)

Each server-paged page (Events, IncomingBatches, OutgoingBatches, Audit) currently has:
```tsx
const [filter, setFilter] = useState<SomeFilter>({ page: 1, pageSize: 25 });
```

Change to this pattern — read the file first to find the exact default values and types used:

```tsx
// 1. Read saved preference (returns defaultValue if prefs not yet loaded or key absent)
const savedFilter   = usePreference<SomeFilter>(PreferenceKeys.xxxFilter,   { page: 1, pageSize: 25 });
const savedPageSize = usePreference<number>    (PreferenceKeys.xxxPageSize,  25);
const { mutate: setPref } = useSetPreference();

// 2. Initialize state; apply saved preference once when it first resolves
const [filter, setFilter] = useState<SomeFilter>({ page: 1, pageSize: savedPageSize });
const prefsApplied = useRef(false);
useEffect(() => {
  if (!prefsApplied.current && savedFilter.page !== undefined) {
    setFilter({ ...savedFilter, page: 1 }); // reset to page 1 on restore
    prefsApplied.current = true;
  }
}, [savedFilter]);

// 3. Persist on change (omit page number from persisted filter)
function handleFilterChange(next: SomeFilter) {
  setFilter(next);
  const { page: _page, ...filterToSave } = next;
  setPref({ key: PreferenceKeys.xxxFilter, value: filterToSave });
  setPref({ key: PreferenceKeys.xxxPageSize, value: next.pageSize });
}
```

**Important:** Do NOT persist the `page` number — always restore to page 1. Persist `pageSize` separately so it survives.

---

- [ ] **Step 1: Read AppLayout.tsx — understand current theme wiring**

Read `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` fully before editing.

Current behaviour: theme stored in `localStorage` at key `msosync.theme`. Toggle button flips `document.documentElement.classList` and writes to `localStorage`.

- [ ] **Step 2: Update AppLayout.tsx — prefetch prefs + sync theme**

Add to AppLayout:

```tsx
import { usePreferences, usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys, Theme } from '../../shared/types/preferences';
```

At the top of the `AppLayout` component body, before any existing state:

```tsx
// Prefetch preferences for the whole session (staleTime: Infinity means one fetch)
usePreferences();

// Read saved theme preference; fall back to current localStorage value
const localTheme = (localStorage.getItem('msosync.theme') as Theme | null) ?? 'light';
const savedTheme = usePreference<Theme>(PreferenceKeys.theme, localTheme);
const { mutate: setPref } = useSetPreference();
```

Find the existing `const [isDark, setIsDark] = useState(...)` initialization. Replace it with:

```tsx
const [isDark, setIsDark] = useState<boolean>(localTheme === 'dark');

// Sync to backend-saved theme once it loads
const themeApplied = useRef(false);
useEffect(() => {
  if (!themeApplied.current && savedTheme !== localTheme) {
    const dark = savedTheme === 'dark';
    setIsDark(dark);
    document.documentElement.classList.toggle('dark', dark);
    themeApplied.current = true;
  }
}, [savedTheme]);
```

Find the theme toggle handler (currently writes to `localStorage`). Add preference persistence alongside the existing localStorage write:

```tsx
// After the existing localStorage.setItem and classList.toggle:
setPref({ key: PreferenceKeys.theme, value: next ? 'dark' : 'light' });
```

Keep the `localStorage.setItem` call — it remains the instant-paint fallback on next load before the API responds.

- [ ] **Step 3: Read EventsPage.tsx and apply integration pattern**

Read `src/MSOSync.Frontend/src/features/events/EventsPage.tsx` fully.

Add imports:
```tsx
import { useRef, useEffect } from 'react';
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';
```

Apply the integration pattern from above using `PreferenceKeys.eventsFilter` and `PreferenceKeys.eventsPageSize`. Find the current `setFilter` calls in filter-change handlers and wrap them with `setPref` calls (omitting the `page` field from the saved filter).

- [ ] **Step 4: Read IncomingBatchesPage.tsx and apply integration pattern**

Read `src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx` (or look for the page in the incoming-batches folder — the exact filename may vary, check the folder).

Apply the same pattern using `PreferenceKeys.incomingFilter` and `PreferenceKeys.incomingPageSize`.

- [ ] **Step 5: Read OutgoingBatchesPage.tsx and apply integration pattern**

Read the outgoing-batches page file. Apply the pattern using `PreferenceKeys.outgoingFilter` and `PreferenceKeys.outgoingPageSize`.

- [ ] **Step 6: Read AuditPage.tsx and apply integration pattern**

Read `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx`.

AuditPage has two tabs (Log and Insights from Epic 11D). The filter state belongs to the Log tab. Apply the pattern using `PreferenceKeys.auditFilter` and `PreferenceKeys.auditPageSize` to the filter that drives `useAuditLog`.

- [ ] **Step 7: Read NodesPage.tsx and add pageSize preference**

Read `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx`.

Nodes is a client-side page (not server-paginated). It uses an AG Grid. Find where the page size is configured (likely in the AG Grid's `paginationPageSize` prop or a `defaultColDef`). Add:

```tsx
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';

// In component:
const savedPageSize = usePreference<number>(PreferenceKeys.nodesPageSize, 25);
const { mutate: setPref } = useSetPreference();
```

Pass `savedPageSize` as the AG Grid `paginationPageSize`. If there's a page-size selector, persist the change:

```tsx
onPaginationChanged={(event) => {
  const size = event.api.paginationGetPageSize();
  setPref({ key: PreferenceKeys.nodesPageSize, value: size });
}}
```

If there is no page-size selector, just set `paginationPageSize={savedPageSize}` as the initial value.

- [ ] **Step 8: Read UsersPage.tsx and add pageSize preference**

Read `src/MSOSync.Frontend/src/features/users/UsersPage.tsx`. Apply the same AG Grid pageSize pattern using `PreferenceKeys.usersPageSize`.

- [ ] **Step 9: Read ParametersPage.tsx — pageSize only**

Read `src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx`. Parameters has no filter. Add `PreferenceKeys.parametersColumns` as the initial `paginationPageSize` (use `PreferenceKeys.parametersColumns` is for column visibility — but in this step focus on pageSize if the grid has one). If Parameters doesn't use pagination, skip.

Actually: check if ParametersPage has pagination. If not, skip this step. If it does, use `PreferenceKeys.nodesPageSize` — wait, that's the wrong key. Use a different existing key:

The correct key for parameters pageSize doesn't exist in PreferenceKeys (we defined `parametersColumns` but not `parametersPageSize`). Since Parameters is a small static list, skip pageSize persistence for Parameters. Only persist column visibility if the page supports column toggling.

If the page has column toggling, persist `PreferenceKeys.parametersColumns`. If not, this step is a no-op.

- [ ] **Step 10: Add Settings section to ProfilePage**

Read `src/MSOSync.Frontend/src/features/profile/ProfilePage.tsx` fully.

Add a "Settings" section below the existing profile content. The section contains three preference controls:

```tsx
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';

// In component:
const autoRefreshEnabled   = usePreference<boolean>(PreferenceKeys.autoRefreshEnabled,  false);
const autoRefreshInterval  = usePreference<number> (PreferenceKeys.autoRefreshInterval, 30);
const notificationsEnabled = usePreference<boolean>(PreferenceKeys.notificationsEnabled, true);
const defaultLandingPage   = usePreference<string> (PreferenceKeys.defaultLandingPage,  '/dashboard');
const { mutate: setPref }  = useSetPreference();
```

Add this JSX section after the existing profile form (use whatever card/section components already exist in the file to match the current styling):

```tsx
<div className="mt-6 border-t pt-6">
  <h3 className="text-sm font-semibold mb-4">Application Settings</h3>

  <div className="space-y-4">
    {/* Default landing page */}
    <div className="flex items-center justify-between">
      <label className="text-sm">Default landing page</label>
      <select
        className="text-sm border rounded px-2 py-1"
        value={defaultLandingPage}
        onChange={e => setPref({ key: PreferenceKeys.defaultLandingPage, value: e.target.value })}
      >
        <option value="/dashboard">Dashboard</option>
        <option value="/events">Events</option>
        <option value="/incoming-batches">Incoming Batches</option>
        <option value="/outgoing-batches">Outgoing Batches</option>
        <option value="/audit">Audit</option>
        <option value="/topology">Topology</option>
        <option value="/nodes">Nodes</option>
      </select>
    </div>

    {/* Auto-refresh */}
    <div className="flex items-center justify-between">
      <label className="text-sm">Auto-refresh dashboard</label>
      <input
        type="checkbox"
        checked={autoRefreshEnabled}
        onChange={e => setPref({ key: PreferenceKeys.autoRefreshEnabled, value: e.target.checked })}
      />
    </div>

    {autoRefreshEnabled && (
      <div className="flex items-center justify-between pl-4">
        <label className="text-sm text-muted-foreground">Refresh every (seconds)</label>
        <input
          type="number"
          min={10}
          max={300}
          className="text-sm border rounded px-2 py-1 w-20"
          value={autoRefreshInterval}
          onChange={e => setPref({ key: PreferenceKeys.autoRefreshInterval, value: Number(e.target.value) })}
        />
      </div>
    )}

    {/* Toast notifications */}
    <div className="flex items-center justify-between">
      <label className="text-sm">Show event notifications</label>
      <input
        type="checkbox"
        checked={notificationsEnabled}
        onChange={e => setPref({ key: PreferenceKeys.notificationsEnabled, value: e.target.checked })}
      />
    </div>
  </div>
</div>
```

Note: `notificationsEnabled` is stored but the actual notification suppression is wired in Task 4 only as storage — the notification logic in the SignalR event router (Epic 11C) reads this preference in a follow-up. For now, just persist the value so it's available.

- [ ] **Step 11: Build check**

```pwsh
cd src/MSOSync.Frontend
npm run build 2>&1 | Select-Object -Last 15
```

Expected: 0 TypeScript errors. Fix any type errors before proceeding.

- [ ] **Step 12: Commit**

```pwsh
git add `
  src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx `
  src/MSOSync.Frontend/src/features/events/EventsPage.tsx `
  src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx `
  src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx `
  src/MSOSync.Frontend/src/features/audit/AuditPage.tsx `
  src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx `
  src/MSOSync.Frontend/src/features/users/UsersPage.tsx `
  src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx `
  src/MSOSync.Frontend/src/features/profile/ProfilePage.tsx

git commit -m "feat(11e): wire preference persistence into 9 pages — filters, pageSize, theme, settings"
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Build: <result>
Concerns: <none or list>
```
