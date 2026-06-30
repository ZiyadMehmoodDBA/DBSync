# Epic 10C — Operational Actions Design

**Date:** 2026-06-30
**Status:** Frozen
**Scope:** Frontend mutations only — write actions wired to existing backend endpoints. No new backend work.

---

## Goal

Add one-click operational actions to the read-only pages built in Epic 10B. No CRUD forms. No new backend endpoints. Pure frontend TanStack Query mutations wired to existing APIs.

---

## Out of Scope (deferred to Epic 10D)

- User create / edit / delete
- Parameter value editing
- Trigger create / update / delete
- Node full metadata edit
- Optimistic updates
- Bulk selection actions

---

## Architecture

**Approach B — Shared ConfirmDialog + feature-local mutations.**

| Layer | Files | Responsibility |
|---|---|---|
| Shared utility | `shared/utils/error.ts` | `getErrorMessage()` — parse Axios/API errors |
| Shared components | `shared/components/actions/ConfirmDialog.tsx` | Presentation-only confirmation dialog |
| | `shared/components/actions/ActionMenu.tsx` | `⋮` dropdown wrapper (shadcn DropdownMenu) |
| | `shared/components/actions/ActionButton.tsx` | Single-action button for single-action pages |
| | `shared/components/actions/index.ts` | Barrel re-export |
| Feature mutations | `features/*/mutations.ts` | `useMutation` hooks with invalidation + toasts |
| Feature columns | `features/*/columns.ts` | Extended with action column / menu cellRenderer |
| App wiring | `app/providers.tsx` | `<Toaster>` mounted once |

**Hard rules for shared action components:**
- No React Query imports
- No API imports
- No toast calls
- No business logic of any kind
- Presentation only

**Feature `mutations.ts` files own:**
- `mutationFn` (API call)
- Cache invalidation via `queryClient.invalidateQueries()`
- Success toast
- Error toast
- Future optimistic update logic (10D)

---

## New Dependencies

```bash
npm install sonner

npx shadcn@latest add dialog
npx shadcn@latest add dropdown-menu
npx shadcn@latest add alert-dialog
```

No additional dependencies beyond these four.

---

## Global Constraints

- TypeScript strict — no `any`, no implicit `any`
- No AG Grid Enterprise imports
- Relative imports only — no `@/` path alias (not configured in this project)
- 12 existing Vitest tests must remain green
- No new frontend unit tests — mutation hooks are validated through manual integration testing against the running API and through existing backend integration tests. Epic 10C introduces no additional frontend unit-test obligations.
- All mutations use `mutateAsync()` (not `mutate()`) so action handlers can await completion before closing dialogs and showing toasts — prevents race conditions between dialog close, cache invalidation, and toast display

---

## Shared Components

### `shared/utils/error.ts`

```typescript
export function getErrorMessage(error: unknown): string
```

Priority order for message extraction:
1. `response.data.detail` — RFC 7807 ProblemDetails (what the .NET backend returns)
2. `response.data.message`
3. `error.message`
4. `"An unexpected error occurred."`

Implementation must support both `AxiosError<ProblemDetails>` and generic `Error` instances without unsafe casting or use of `any`.

### `shared/components/actions/ConfirmDialog.tsx`

Props:
```typescript
interface ConfirmDialogProps {
  open: boolean;
  title: string;
  description: string;
  confirmLabel?: string;       // default: "Confirm"
  variant?: 'default' | 'destructive';  // default: 'default'
  loading?: boolean;
  onConfirm: () => void;
  onOpenChange: (open: boolean) => void;
}
```

Renders shadcn `AlertDialog`. Uses destructive button styling when `variant="destructive"`. Both variants use `AlertDialog` — the variant drives button color only.

### `shared/components/actions/ActionMenu.tsx`

Props:
```typescript
interface ActionMenuItem {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  variant?: 'default' | 'destructive';
}

interface ActionMenuProps {
  items: ActionMenuItem[];
}
```

Renders a `⋮` Button that opens a shadcn `DropdownMenu`. Used on pages with 2+ actions per row (Nodes, Triggers, Outgoing Batches).

### `shared/components/actions/ActionButton.tsx`

Props:
```typescript
interface ActionButtonProps {
  label: string;
  onClick: () => void;
  loading?: boolean;
  variant?: 'default' | 'destructive';
}
```

Simple shadcn `Button` wrapper. Used on pages with a single action per row (Locks).

### `shared/components/actions/index.ts`

```typescript
export * from './ActionButton';
export * from './ActionMenu';
export * from './ConfirmDialog';
```

---

## Toaster Placement

`app/providers.tsx` — mount once, globally:

```tsx
import { Toaster } from 'sonner';

// Inside the provider tree, after RouterProvider:
<Toaster richColors closeButton position="bottom-right" />
```

No per-page toaster setup.

---

## Per-Feature Action Scope

### Locks (`features/locks/`)

**Backend endpoint:** `DELETE /api/v1/locks/{lockName}` — AdminOnly

| Action | UX | Confirmation | Variant | Cache invalidated |
|---|---|---|---|---|
| Release lock | `ActionButton` ("Release") in column | Yes | destructive | `queryKeys.locks()` |

**New files:**
- `features/locks/mutations.ts` — `useReleaseLockMutation()`

**Modified files:**
- `features/locks/columns.ts` — add Actions column with `ActionButton` cellRenderer

---

### Nodes (`features/nodes/`)

**Backend endpoints:**
- `POST /api/v1/nodes/{nodeId}/enable` — OperatorOrAbove
- `POST /api/v1/nodes/{nodeId}/disable` — OperatorOrAbove
- `POST /api/v1/nodes/registrations/{requestId}/approve` — OperatorOrAbove

| Action | UX | Confirmation | Variant | Cache invalidated |
|---|---|---|---|---|
| Enable node | `ActionMenu` item | Yes | default | `queryKeys.nodes()`, `queryKeys.dashboardSummary()`, `queryKeys.metricsSummary()` |
| Disable node | `ActionMenu` item | Yes | destructive | `queryKeys.nodes()`, `queryKeys.dashboardSummary()`, `queryKeys.metricsSummary()` |
| Approve registration | `ActionMenu` item | Yes | default | `queryKeys.nodes()`, `queryKeys.dashboardSummary()`, `queryKeys.metricsSummary()` |

**New files:**
- `features/nodes/mutations.ts` — `useEnableNodeMutation()`, `useDisableNodeMutation()`, `useApproveRegistrationMutation()`

**Modified files:**
- `features/nodes/columns.ts` — add Actions column with `ActionMenu` cellRenderer
- `features/nodes/NodesPage.tsx` — manage `confirmState` for the active action

---

### Triggers (`features/triggers/`)

**Backend endpoints:**
- `POST /api/v1/triggers/{triggerId}/enable` — OperatorOrAbove
- `POST /api/v1/triggers/{triggerId}/disable` — OperatorOrAbove
- `POST /api/v1/triggers/{triggerId}/rebuild` — OperatorOrAbove
- `POST /api/v1/triggers/{triggerId}/verify` — Authorize (any authenticated user)

| Action | UX | Confirmation | Variant | Cache invalidated |
|---|---|---|---|---|
| Enable trigger | `ActionMenu` item | Yes | default | `queryKeys.triggers()` |
| Disable trigger | `ActionMenu` item | Yes | destructive | `queryKeys.triggers()` |
| Rebuild trigger | `ActionMenu` item | Yes — warning: recreates DB trigger | destructive | `queryKeys.triggers()` |
| Verify trigger | `ActionMenu` item | **No** — informational toast only | — | none (read-like operation) |

Verify fires immediately on click and shows `toast.info("Trigger verified successfully.")` or `toast.error(getErrorMessage(error))`.

**New files:**
- `features/triggers/mutations.ts` — `useEnableTriggerMutation()`, `useDisableTriggerMutation()`, `useRebuildTriggerMutation()`, `useVerifyTriggerMutation()`

**Modified files:**
- `features/triggers/columns.ts` — add Actions column with `ActionMenu` cellRenderer
- `features/triggers/TriggersPage.tsx` — manage `confirmState` for the active action

---

### Outgoing Batches (`features/outgoing-batches/`)

**Backend endpoints:**
- `POST /api/v1/outgoing-batches/{batchId}/retry` — OperatorOrAbove
- `POST /api/v1/outgoing-batches/retry-all` — OperatorOrAbove

| Action | UX | Confirmation | Cache invalidated |
|---|---|---|---|
| Retry single batch | `ActionMenu` per row | No | `queryKeys.outgoingBatches(filter)` |
| Retry all | Button in page header | No | `queryKeys.outgoingBatches()`, `queryKeys.dashboardSummary()`, `queryKeys.metricsSummary()` |

**Loading state:** Retry All button is disabled while a retry-all mutation is in flight (`mutation.isPending`). Same pattern applies to per-row retry buttons — the row's action is disabled while the single-batch retry is in flight.

**New files:**
- `features/outgoing-batches/mutations.ts` — `useRetryBatchMutation()`, `useRetryAllBatchesMutation()`

**Modified files:**
- `features/outgoing-batches/columns.ts` — add Actions column with `ActionMenu` cellRenderer
- `features/outgoing-batches/OutgoingBatchesPage.tsx` — add Retry All button in header

---

## Mutation Pattern

All mutation hooks follow this shape:

```typescript
export function useDisableNodeMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (nodeId: string) => disableNode(nodeId),
    onSuccess: () => {
      toast.success('Node disabled');
      void queryClient.invalidateQueries({ queryKey: queryKeys.nodes() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.metricsSummary() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}
```

**Calling pattern in page/column components:**

```tsx
const mutation = useDisableNodeMutation();

const handleDisable = async (nodeId: string) => {
  await mutation.mutateAsync(nodeId);
  setConfirmOpen(false);
};
```

`mutateAsync()` is required — not `mutate()` — so the dialog close and success toast are sequenced after request completion.

---

## Cache Invalidation Policy

Mutations invalidate only the minimal affected query keys plus dashboard/metrics summaries when operational counts may change. Avoid `queryClient.invalidateQueries()` without a specific key.

| Scenario | Keys to invalidate |
|---|---|
| Lock released | `queryKeys.locks()` |
| Node enabled/disabled/approved | `queryKeys.nodes()`, `queryKeys.dashboardSummary()`, `queryKeys.metricsSummary()` |
| Trigger enabled/disabled/rebuilt | `queryKeys.triggers()` |
| Trigger verified | none |
| Batch retry (single) | `queryKeys.outgoingBatches(filter)` |
| Batch retry-all | `queryKeys.outgoingBatches()`, `queryKeys.dashboardSummary()`, `queryKeys.metricsSummary()` |

---

## New API Functions Required

The following must be added to `shared/api/` (currently GET-only):

| File | New functions |
|---|---|
| `shared/api/locks.ts` | `releaseLock(lockName: string)` |
| `shared/api/nodes.ts` | `enableNode(nodeId)`, `disableNode(nodeId)`, `approveRegistration(requestId)` |
| `shared/api/triggers.ts` | `enableTrigger(id)`, `disableTrigger(id)`, `rebuildTrigger(id)`, `verifyTrigger(id)` |
| `shared/api/batches.ts` | `retryBatch(batchId: number)`, `retryAllBatches()` |

All return `Promise<void>` (backend returns `204 No Content` for operational actions).

---

## File Change Summary

**New files (14):**
- `shared/utils/error.ts`
- `shared/components/actions/ConfirmDialog.tsx`
- `shared/components/actions/ActionMenu.tsx`
- `shared/components/actions/ActionButton.tsx`
- `shared/components/actions/index.ts`
- `features/locks/mutations.ts`
- `features/nodes/mutations.ts`
- `features/triggers/mutations.ts`
- `features/outgoing-batches/mutations.ts`
- `components/ui/dialog.tsx` (shadcn generated)
- `components/ui/dropdown-menu.tsx` (shadcn generated)
- `components/ui/alert-dialog.tsx` (shadcn generated)

**Modified files (8):**
- `app/providers.tsx` — add `<Toaster>`
- `shared/api/locks.ts` — add mutation functions
- `shared/api/nodes.ts` — add mutation functions
- `shared/api/triggers.ts` — add mutation functions
- `shared/api/batches.ts` — add `retryBatch`, `retryAllBatches` (new file: `shared/api/outgoing-batches.ts` if batches API is split)
- `features/locks/columns.ts` — add Actions column
- `features/nodes/columns.ts` + `NodesPage.tsx` — action menu + confirm state
- `features/triggers/columns.ts` + `TriggersPage.tsx` — action menu + confirm state
- `features/outgoing-batches/columns.ts` + `OutgoingBatchesPage.tsx` — action menu + retry-all button

**`package.json`:** `sonner` added

---

## API Route Reference

| Feature | Method | Route | Auth |
|---|---|---|---|
| Locks | DELETE | `/api/v1/locks/{lockName}` | AdminOnly |
| Nodes | POST | `/api/v1/nodes/{nodeId}/enable` | OperatorOrAbove |
| Nodes | POST | `/api/v1/nodes/{nodeId}/disable` | OperatorOrAbove |
| Nodes | POST | `/api/v1/nodes/registrations/{requestId}/approve` | OperatorOrAbove |
| Triggers | POST | `/api/v1/triggers/{triggerId}/enable` | OperatorOrAbove |
| Triggers | POST | `/api/v1/triggers/{triggerId}/disable` | OperatorOrAbove |
| Triggers | POST | `/api/v1/triggers/{triggerId}/rebuild` | OperatorOrAbove |
| Triggers | POST | `/api/v1/triggers/{triggerId}/verify` | Authorize |
| Batches | POST | `/api/v1/outgoing-batches/{batchId}/retry` | OperatorOrAbove |
| Batches | POST | `/api/v1/outgoing-batches/retry-all` | OperatorOrAbove |
