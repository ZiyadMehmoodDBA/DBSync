# Task 3: Frontend Shared Infrastructure

**Part of:** Epic 11E — User Preferences & Saved Workspaces  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11e-user-preferences-design.md`  
**Depends on:** Task 2 (backend endpoints must exist)

## Files

**Create:**
- `src/MSOSync.Frontend/src/shared/types/preferences.ts`
- `src/MSOSync.Frontend/src/shared/api/preferences.ts`
- `src/MSOSync.Frontend/src/shared/hooks/usePreferences.ts`

**Modify:**
- `src/MSOSync.Frontend/src/shared/types/index.ts` — add `export * from './preferences'`
- `src/MSOSync.Frontend/src/shared/queryKeys.ts` — add `userPreferences` key

## Interfaces Produced (consumed by Task 4)

```typescript
// PreferenceKeys — typed string constants (no enum)
PreferenceKeys.eventsFilter, .eventsPageSize, .eventsSort, .eventsColumns,
PreferenceKeys.incomingFilter, .incomingPageSize, .incomingSort, .incomingColumns,
PreferenceKeys.outgoingFilter, .outgoingPageSize, .outgoingSort, .outgoingColumns,
PreferenceKeys.auditFilter, .auditPageSize, .auditSort, .auditColumns,
PreferenceKeys.nodesColumns, .nodesPageSize,
PreferenceKeys.usersColumns, .usersPageSize,
PreferenceKeys.parametersColumns,
PreferenceKeys.theme, .defaultLandingPage, .autoRefreshEnabled,
PreferenceKeys.autoRefreshInterval, .notificationsEnabled

// Hooks
usePreferences():      UseQueryResult<Record<string, unknown>>
usePreference<T>(key, defaultValue): T
useSetPreference():    UseMutationResult with { key: string; value: unknown }
useDeletePreference(): UseMutationResult with key: string
```

---

## Global Constraints

- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword; use `as const`
- All imports relative — no `@/` aliases
- No new npm packages
- TanStack Query v5 API

---

- [ ] **Step 1: Create preferences.ts types**

```typescript
// src/MSOSync.Frontend/src/shared/types/preferences.ts
export const PreferenceKeys = {
  // Events page
  eventsFilter:          'page.events.filter',
  eventsPageSize:        'page.events.pageSize',
  eventsSort:            'page.events.sort',
  eventsColumns:         'page.events.columns',
  // Incoming batches page
  incomingFilter:        'page.incoming-batches.filter',
  incomingPageSize:      'page.incoming-batches.pageSize',
  incomingSort:          'page.incoming-batches.sort',
  incomingColumns:       'page.incoming-batches.columns',
  // Outgoing batches page
  outgoingFilter:        'page.outgoing-batches.filter',
  outgoingPageSize:      'page.outgoing-batches.pageSize',
  outgoingSort:          'page.outgoing-batches.sort',
  outgoingColumns:       'page.outgoing-batches.columns',
  // Audit page
  auditFilter:           'page.audit.filter',
  auditPageSize:         'page.audit.pageSize',
  auditSort:             'page.audit.sort',
  auditColumns:          'page.audit.columns',
  // Nodes page
  nodesColumns:          'page.nodes.columns',
  nodesPageSize:         'page.nodes.pageSize',
  // Users page
  usersColumns:          'page.users.columns',
  usersPageSize:         'page.users.pageSize',
  // Parameters page
  parametersColumns:     'page.parameters.columns',
  // UI preferences
  theme:                 'ui.theme',
  defaultLandingPage:    'ui.defaultLandingPage',
  autoRefreshEnabled:    'ui.autoRefresh.enabled',
  autoRefreshInterval:   'ui.autoRefresh.intervalSeconds',
  notificationsEnabled:  'ui.notifications.enabled',
} as const;

export type PreferenceKey   = typeof PreferenceKeys[keyof typeof PreferenceKeys];
export type Theme           = 'light' | 'dark';
export type SortPreference  = { field: string; direction: 'asc' | 'desc' };
```

- [ ] **Step 2: Add export to types/index.ts**

Open `src/MSOSync.Frontend/src/shared/types/index.ts`. Add at the end:

```typescript
export * from './preferences';
```

- [ ] **Step 3: Add userPreferences query key**

Open `src/MSOSync.Frontend/src/shared/queryKeys.ts`. Add inside the `queryKeys` object:

```typescript
userPreferences: () => ['user-preferences'] as const,
```

(Use `'user-preferences'` — NOT `['audit', 'preferences']` — to avoid any invalidation coupling with audit keys.)

- [ ] **Step 4: Create preferences API functions**

```typescript
// src/MSOSync.Frontend/src/shared/api/preferences.ts
import client from './client';

export async function getPreferences(): Promise<Record<string, unknown>> {
  return client.get<Record<string, unknown>>('/preferences').then(r => r.data);
}

export async function upsertPreference(key: string, value: unknown): Promise<void> {
  await client.put(`/preferences/${encodeURIComponent(key)}`, value);
}

export async function bulkUpsertPreferences(
  prefs: Record<string, unknown>,
): Promise<void> {
  await client.put('/preferences', prefs);
}

export async function deletePreference(key: string): Promise<void> {
  await client.delete(`/preferences/${encodeURIComponent(key)}`);
}
```

- [ ] **Step 5: Create usePreferences hook family**

```typescript
// src/MSOSync.Frontend/src/shared/hooks/usePreferences.ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getPreferences,
  upsertPreference,
  deletePreference,
} from '../api/preferences';
import { queryKeys } from '../queryKeys';

export function usePreferences() {
  return useQuery({
    queryKey: queryKeys.userPreferences(),
    queryFn:  getPreferences,
    staleTime: Infinity,
  });
}

export function usePreference<T>(key: string, defaultValue: T): T {
  const { data } = usePreferences();
  if (data === undefined || !(key in data)) return defaultValue;
  return data[key] as T;
}

export function useSetPreference() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ key, value }: { key: string; value: unknown }) =>
      upsertPreference(key, value),
    onMutate: async ({ key, value }) => {
      await queryClient.cancelQueries({ queryKey: queryKeys.userPreferences() });
      const previous = queryClient.getQueryData<Record<string, unknown>>(
        queryKeys.userPreferences(),
      );
      queryClient.setQueryData<Record<string, unknown>>(
        queryKeys.userPreferences(),
        old => ({ ...old, [key]: value }),
      );
      return { previous };
    },
    onError: (_err, _vars, context) => {
      if (context?.previous !== undefined) {
        queryClient.setQueryData(queryKeys.userPreferences(), context.previous);
      }
    },
  });
}

export function useDeletePreference() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (key: string) => deletePreference(key),
    onMutate: async (key) => {
      await queryClient.cancelQueries({ queryKey: queryKeys.userPreferences() });
      const previous = queryClient.getQueryData<Record<string, unknown>>(
        queryKeys.userPreferences(),
      );
      queryClient.setQueryData<Record<string, unknown>>(
        queryKeys.userPreferences(),
        (old) => {
          const next = { ...old };
          delete next[key];
          return next;
        },
      );
      return { previous };
    },
    onError: (_err, _vars, context) => {
      if (context?.previous !== undefined) {
        queryClient.setQueryData(queryKeys.userPreferences(), context.previous);
      }
    },
  });
}
```

- [ ] **Step 6: Build check**

```pwsh
cd src/MSOSync.Frontend
npm run build 2>&1 | Select-Object -Last 10
```

Expected: built in N seconds, 0 TypeScript errors. Pre-existing chunk-size and SignalR warnings are acceptable.

- [ ] **Step 7: Commit**

```pwsh
git add `
  src/MSOSync.Frontend/src/shared/types/preferences.ts `
  src/MSOSync.Frontend/src/shared/types/index.ts `
  src/MSOSync.Frontend/src/shared/queryKeys.ts `
  src/MSOSync.Frontend/src/shared/api/preferences.ts `
  src/MSOSync.Frontend/src/shared/hooks/usePreferences.ts

git commit -m "feat(11e): add preferences types, API functions, and usePreferences/useSetPreference hooks"
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Build: <result>
Concerns: <none or list>
```
