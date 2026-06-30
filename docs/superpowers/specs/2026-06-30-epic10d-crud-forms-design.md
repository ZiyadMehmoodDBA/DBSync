# Epic 10D — CRUD Forms Design

**Goal:** Add create, edit, and delete capabilities to the administrative pages built in Epic 10B (read-only tables) and extended in Epic 10C (operational actions). All six entities — Parameters, Users, Triggers, Channels, Routers, Nodes — get forms backed by existing backend endpoints.

**Approach:** shadcn Dialog for all forms + react-hook-form + Zod. Feature-local schemas, mutations, and dialog components. Shared presentation-only form infrastructure in `shared/components/forms/`.

**No new backend work.** All endpoints exist from Epics 4 and 8–9.

---

## Global Constraints

- TypeScript strict; no `any`; relative imports only (no `@/` alias in our code)
- `mutateAsync()` everywhere — no `mutate()` calls
- No optimistic updates — all mutations rely on server acknowledgment + query invalidation
- No new Vitest tests — build clean + 12/12 existing tests pass = done
- Shared form components are presentation-only: no React Query, no API imports, no toast calls
- All Zod schemas live in `schemas.ts` per feature — not inline in Dialog components
- shadcn Dialog is the only form presentation — no inline grid editing, no Sheet
- All dialog forms reset state on open/close via `useEffect`
- All destructive actions use `ConfirmDialog` from 10C — never inside the form
- `form.formState.isSubmitting` drives Submit button disabled/loading — no custom `isLoading` flags

---

## Architecture

### Shared form infrastructure

New directory: `src/MSOSync.Frontend/src/shared/components/forms/`

| File | Responsibility |
|---|---|
| `EntityDialog.tsx` | Wraps shadcn `Dialog`; owns title, description, scrollable body, `max-w-lg` width, focus trap, close behavior. Does NOT own forms, validation, buttons, or API errors. |
| `FormActions.tsx` | Footer row: Cancel + Submit. Accepts `loading` (from `form.formState.isSubmitting`), `onCancel`, `submitLabel`. |
| `FormError.tsx` | Renders a single API error string in a red callout above `FormActions`. Receives `error: string \| null`. |
| `FormSection.tsx` | Optional visual grouping with a heading — `<FormSection title="Sync Options">`. Keeps 5–6 field forms readable. |
| `index.ts` | Barrel: `export * from './EntityDialog'` etc. |

### Feature-local pattern

Each entity gets:

```
features/<entity>/
  schemas.ts         ← Zod schemas + inferred TypeScript types
  mutations.ts       ← create/update/delete mutation hooks (+ 10C hooks if they exist)
  <Entity>Dialog.tsx ← Dialog + form + submission
  columns.ts         ← factory function with action callbacks
  <Entity>Grid.tsx   ← updated props
  <Entity>Page.tsx   ← Add button + dialog/confirm state management
```

### Mutation hook contract

Form mutations do NOT include `onError`. The hook owns cache invalidation only; the Dialog component owns toasts, close behavior, and inline error display.

```ts
export function useCreateUserMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateUserRequest) => createUser(data),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.users() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
    },
    // no onError — caller handles it
  });
}
```

### Dialog submission invariant

```tsx
const onSubmit = async (values: FormValues) => {
  setApiError(null);                        // clear stale error first
  try {
    await mutateAsync(values);
    toast.success('...');
    onOpenChange(false);                    // close ONLY on success
  } catch (error) {
    setApiError(getErrorMessage(error));    // show inline, dialog stays open
  }
};
```

### Dialog reset invariant

Every dialog resets form state and API error on both open and close:

```tsx
useEffect(() => {
  if (open) {
    form.reset(defaultValues);
    setApiError(null);
  } else {
    form.reset();
    setApiError(null);
  }
}, [open, defaultValues, form]);
```

`defaultValues` is derived from `initialValues` prop (edit mode) or static defaults (create mode).

### Default values convention

All dialogs derive defaults via a feature-local `getDefaultValues` helper:

```ts
// features/users/schemas.ts
export function getDefaultValues(initialValues?: UserSummaryDto, mode?: 'create' | 'edit') {
  if (mode === 'edit' && initialValues) {
    return { enabled: initialValues.enabled, newPassword: '' };
  }
  return { username: '', password: '', enabled: true };
}
```

Used in the dialog as:

```tsx
const defaultValues = useMemo(
  () => getDefaultValues(initialValues, mode),
  [initialValues, mode],
);
```

This prevents each dialog from reimplementing initialization logic inline.

### Mode pattern

All dialogs use a single component with `mode: 'create' | 'edit'` prop:

```ts
interface UserDialogProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValues?: UserSummaryDto;
  onOpenChange(open: boolean): void;
}
```

The form resolves the active schema and mutation based on `mode`.

---

## Dependencies

### npm (install)

```
react-hook-form
zod
@hookform/resolvers
```

### shadcn (add via CLI)

```
dialog
form
label
input
select
checkbox
textarea
```

---

## Per-Entity Specification

### Parameters — edit value only

**Operations:** Edit value only. No create, no delete (parameters are system config).

**Backend:** `PUT /api/v1/parameters/{name}` → `{ value: string }`

**Form fields (edit mode):**
- `value`: text input; `type="password"` + `autoComplete="off"` when `isSecret === true`
- Do NOT prefill masked values — leave the input empty for secrets

**Zod schema:**
```ts
export const updateParameterSchema = z.object({
  value: z.string().trim().min(1, 'Value is required'),
});
export type UpdateParameterForm = z.infer<typeof updateParameterSchema>;
```

**Column change:** `parameterColumns` const → `makeParameterColumns(onEdit)` factory. Adds Actions column with single "Edit" item. No delete item.

**Cache invalidation:** `queryKeys.parameters()`

**Page changes:** No "Add" button. Manages `editState: ParameterDto | null`. ParametersGrid gains `onEdit` prop.

---

### Users — create, edit, deactivate

**Operations:** Create, Edit, Deactivate (soft delete).

**Backend:**
- `POST /api/v1/users` → `{ username, password, enabled }`
- `PUT /api/v1/users/{userId}` → `{ enabled?, newPassword? }`
- `DELETE /api/v1/users/{userId}`

**Form fields:**
- Create: `username` (text), `password` (password), `enabled` (checkbox, default `true`)
- Edit: `enabled` (checkbox), `newPassword` (password, optional — send `undefined` if empty)

**Zod schemas:**
```ts
export const createUserSchema = z.object({
  username: z.string().trim().min(3).max(100),
  password: z.string().min(8),
  enabled: z.boolean(),
});
export type CreateUserForm = z.infer<typeof createUserSchema>;

export const updateUserSchema = z.object({
  enabled: z.boolean(),
  newPassword: z.string().min(8).optional().or(z.literal('')),
});
export type UpdateUserForm = z.infer<typeof updateUserSchema>;
```

Submission mapping for update: `newPassword: values.newPassword || undefined` — empty string is not sent to the API.

**Self-deactivation guard:** Deactivate action is hidden/disabled for the currently authenticated user (compare `userId` with the value from `useAuth()`). The backend enforces this, but the UI prevents unnecessary 403 responses.

**Column change:** `userColumns` const → `makeUserColumns(onEdit, onDeactivate)` factory. Adds Actions with "Edit" + "Deactivate" (destructive).

**Cache invalidation:** `queryKeys.users()` + `queryKeys.dashboardSummary()`

**Page changes:** "Add User" button in page header. Manages `createOpen: boolean`, `editState: UserSummaryDto | null`, `deactivateState: UserSummaryDto | null`. UsersGrid gains `onEdit + onDeactivate` props.

---

### Channels — create, edit, delete

**Operations:** Create, Edit, Delete.

**Backend:**
- `POST /api/v1/channels` → `{ channelId, priority, batchSize, maxBatchToSend, maxDataSize }`
- `PUT /api/v1/channels/{channelId}` → same minus `channelId`
- `DELETE /api/v1/channels/{channelId}`

**Form fields:**
- Create: `channelId` (text), `priority` (number), `batchSize` (number, default 1000), `maxBatchToSend` (number, default 10), `maxDataSize` (number, default 1048576)
- Edit: same minus `channelId` (shown read-only in dialog title/description)
- All number inputs use `inputMode="numeric"`

**Zod schemas:**
```ts
export const createChannelSchema = z.object({
  channelId: z.string().trim().min(1),
  priority: z.coerce.number().int().min(0).max(100),
  batchSize: z.coerce.number().int().min(1).max(1_000_000),
  maxBatchToSend: z.coerce.number().int().min(1).max(10_000),
  maxDataSize: z.coerce.number().int().min(1),
});
export type CreateChannelForm = z.infer<typeof createChannelSchema>;

export const updateChannelSchema = createChannelSchema.omit({ channelId: true });
export type UpdateChannelForm = z.infer<typeof updateChannelSchema>;
```

**Column change:** `channelColumns` const → `makeChannelColumns(onEdit, onDelete)` factory. Adds Actions with "Edit" + "Delete" (destructive).

**Cache invalidation:** `queryKeys.channels()` + `queryKeys.topologySummary()` + `queryKeys.topologyGroups()`

**Page changes:** "Add Channel" button in header. Manages `createOpen`, `editState: ChannelDto | null`, `deleteState: ChannelDto | null`. ChannelsGrid gains `onEdit + onDelete` props.

---

### Trigger source-table helpers

The backend `CreateTriggerRequest` / `UpdateTriggerRequest` use a single `sourceTable: string` (e.g. `"dbo.Orders"`). The form collects `schemaName` + `tableName` separately. Centralize the mapping in one place:

```ts
// features/triggers/utils.ts
export function toSourceTable(schemaName: string, tableName: string): string {
  return `${schemaName}.${tableName}`;
}

export function fromSourceTable(sourceTable: string): { schemaName: string; tableName: string } {
  const dot = sourceTable.lastIndexOf('.');
  if (dot === -1) return { schemaName: 'dbo', tableName: sourceTable };
  return { schemaName: sourceTable.slice(0, dot), tableName: sourceTable.slice(dot + 1) };
}
```

`TriggerDialog` uses `fromSourceTable(trigger.tableName)` — wait, the `TriggerDto` already has separate `schemaName` + `tableName` fields. `fromSourceTable` is only needed if the Dto ever returns a merged string. Since `TriggerDto` has both fields, `toSourceTable` is the only required helper for submit mapping.

`fromSourceTable` is included for completeness and forward compatibility.

---

### Triggers — create, edit, delete (extends 10C)

**Operations:** Create, Edit, Delete. Enable/Disable/Rebuild/Verify from 10C are preserved unchanged.

**Backend:**
- `POST /api/v1/triggers` → `{ triggerId, schemaName, tableName, channelId, syncOnInsert, syncOnUpdate, syncOnDelete }`

  *Note: The backend `CreateTriggerRequest` uses `sourceTable` as a single `"schema.table"` string. The form collects `schemaName` + `tableName` separately and merges before submit: `sourceTable: \`${values.schemaName}.${values.tableName}\``.*

- `PUT /api/v1/triggers/{triggerId}` → same minus `triggerId` (merged `sourceTable`)
- `DELETE /api/v1/triggers/{triggerId}`

**Form fields:**
- Create: `triggerId` (text), `schemaName` (text, e.g. `dbo`), `tableName` (text, e.g. `Orders`), `channelId` (Select from `useChannels()`), `syncOnInsert` (checkbox, default true), `syncOnUpdate` (checkbox, true), `syncOnDelete` (checkbox, true)
- Edit: same minus `triggerId` (shown read-only)

**Zod schemas:**
```ts
export const createTriggerSchema = z.object({
  triggerId: z.string().trim().min(1),
  schemaName: z.string().trim().min(1),
  tableName: z.string().trim().min(1),
  channelId: z.string().min(1),
  syncOnInsert: z.boolean(),
  syncOnUpdate: z.boolean(),
  syncOnDelete: z.boolean(),
}).refine(
  x => x.syncOnInsert || x.syncOnUpdate || x.syncOnDelete,
  { message: 'At least one sync operation must be enabled.' }
);
export type CreateTriggerForm = z.infer<typeof createTriggerSchema>;

export const updateTriggerSchema = createTriggerSchema.omit({ triggerId: true });
export type UpdateTriggerForm = z.infer<typeof updateTriggerSchema>;
```

**Column change:** `makeTriggersColumns(onAction, onVerify)` → `makeTriggersColumns(onAction, onVerify, onEdit, onDelete)`. Adds "Edit" + "Delete" (destructive) to existing ActionMenu. Existing 4 items unchanged.

**Cache invalidation:** `queryKeys.triggers()` + `queryKeys.topologySummary()` + `queryKeys.topologyGroups()`

**Page changes:** "Add Trigger" button in header. Manages `createOpen`, `editState: TriggerDto | null`, `deleteState: TriggerDto | null` alongside existing `confirmState` for 10C actions. TriggersGrid gains `onEdit + onDelete` props.

---

### Routers — create, edit, delete (new in 10D)

**Operations:** Create, Edit, Delete. No 10C mutations exist for Routers.

**Backend:**
- `POST /api/v1/routers` → `{ routerId, sourceNodeGroup, targetNodeGroup, routerType }`
- `PUT /api/v1/routers/{routerId}` → same minus `routerId`
- `DELETE /api/v1/routers/{routerId}`

**Form fields:**
- Create: `routerId` (text), `sourceNodeGroup` (Select from `useNodeGroups()`), `targetNodeGroup` (Select from same list), `routerType` (Select, options from `ROUTER_TYPES`)
- Edit: same minus `routerId` (shown read-only)
- Using Select for group fields prevents typo-driven topology corruption and keeps UI consistent with the Nodes edit form.

**Zod schemas:**
```ts
export const ROUTER_TYPES = ['default'] as const;
export type RouterType = typeof ROUTER_TYPES[number];

export const createRouterSchema = z.object({
  routerId: z.string().trim().min(1),
  sourceNodeGroup: z.string().trim().min(1),
  targetNodeGroup: z.string().trim().min(1),
  routerType: z.enum(ROUTER_TYPES),
}).refine(
  x => x.sourceNodeGroup !== x.targetNodeGroup,
  { message: 'Source and target groups must differ.', path: ['targetNodeGroup'] }
);
export type CreateRouterForm = z.infer<typeof createRouterSchema>;

export const updateRouterSchema = createRouterSchema.omit({ routerId: true });
export type UpdateRouterForm = z.infer<typeof updateRouterSchema>;
```

**Column change:** `routerColumns` const → `makeRouterColumns(onEdit, onDelete)` factory. Adds Actions column.

**Cache invalidation:** `queryKeys.routers()` + `queryKeys.topologySummary()` + `queryKeys.topologyGroups()`

**Page changes:** "Add Router" button in header. Manages `createOpen`, `editState`, `deleteState`. RoutersGrid gains `onEdit + onDelete` props.

---

### Nodes — edit metadata only (extends 10C)

**Operations:** Edit metadata only. No create (nodes self-register). No delete from this page.

**Backend:** `PUT /api/v1/nodes/{nodeId}` → `{ groupId, syncUrl, heartbeatInterval }`

**Form fields (edit mode only):**
- `groupId`: Select populated from `useNodeGroups()` — not free text
- `syncUrl`: text input, validated as URL (must include `http://` or `https://`)
- `heartbeatInterval`: number input, `inputMode="numeric"`, unit is minutes

**Zod schema:**
```ts
export const updateNodeSchema = z.object({
  groupId: z.string().min(1),
  syncUrl: z.string().url('Must be a valid URL including http:// or https://'),
  heartbeatInterval: z.coerce.number().int().min(1).max(1440),
});
export type UpdateNodeForm = z.infer<typeof updateNodeSchema>;
```

**Column change:** `makeNodeColumns(onAction)` → `makeNodeColumns(onAction, onEdit)`. Adds "Edit" to existing ActionMenu alongside Enable/Disable/Approve Registration.

**Cache invalidation:** `queryKeys.nodes()` + `queryKeys.topologySummary()` + `queryKeys.topologyGroups()` + `queryKeys.metricsSummary()` (consistent with 10C node operational actions)

**Page changes:** No "Add" button. Manages `editState: NodeDto | null` alongside existing `confirmState`. NodesGrid gains `onEdit` prop.

---

## Acceptance Criteria

### Per-action matrix

| Action | Trigger | Expected outcome |
|---|---|---|
| Create | "Add X" button → fill form → Submit | Success toast, dialog closes, grid refreshes |
| Edit | ActionMenu → Edit → form pre-populated → modify → Submit | Success toast, dialog closes, grid refreshes |
| Delete | ActionMenu → Delete → ConfirmDialog → Confirm | Success toast, grid refreshes |
| API error | Submit → backend returns 4xx | Dialog stays open, error shown above footer |
| Validation error | Submit with invalid field | react-hook-form shows per-field errors, no API call |
| Self-deactivation | Current user row → ActionMenu | "Deactivate" hidden or disabled |
| Cancel | Click Cancel or ✕ | No mutation, no toast, dialog closes |
| Reopen dialog | Open after previous error | Clean form state, no stale errors |
| Double submit | Click Submit twice rapidly | Only one request issued; Submit disabled while `isSubmitting` |

### Epic completion gates

```
✓ npm run build exits 0
✓ npm test -- --run remains 12/12 passing
✓ No TypeScript errors
✓ No use of any
✓ All CRUD dialogs reset cleanly on close/reopen
✓ All mutations use mutateAsync(), never mutate()
✓ All mutations invalidate the correct query keys
✓ No optimistic updates
✓ All destructive actions use ConfirmDialog
✓ All success paths emit Sonner success toasts
✓ All API failures remain inline inside dialogs (no error toasts for form mutations)
✓ form.formState.isSubmitting drives Submit button state (no custom flags)
✓ apiError cleared before each submission attempt
```

---

## Task Plan

| Task | Description |
|---|---|
| 1 | Shared form infrastructure: install deps, shadcn installs, EntityDialog + FormActions + FormError + FormSection + index.ts; establish `getDefaultValues` convention and document form reset pattern |
| 2 | Parameters edit: API function, schema, mutation, ParameterDialog, columns factory, ParametersPage wire |
| 3 | Users CRUD: 3 API functions, 2 schemas, 3 mutations, UserDialog, columns factory, UsersGrid + UsersPage |
| 4 | Channels CRUD: 3 API functions, 2 schemas, 3 mutations, ChannelDialog, columns factory, ChannelsGrid + ChannelsPage |
| 5 | Triggers CRUD (extend 10C): 3 API functions, 2 schemas, 3 new mutations, TriggerDialog (channels select), extend columns factory + TriggersGrid + TriggersPage |
| 6 | Routers CRUD: 3 API functions, 2 schemas, 3 mutations, RouterDialog, columns factory, RoutersGrid + RoutersPage |
| 7 | Nodes edit (extend 10C): 1 API function, 1 schema, 1 mutation, NodeDialog (groupId Select), extend columns factory + NodesGrid + NodesPage |
