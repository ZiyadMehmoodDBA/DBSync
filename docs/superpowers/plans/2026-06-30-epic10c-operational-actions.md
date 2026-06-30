# Epic 10C — Operational Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one-click operational actions (release lock, enable/disable node, approve registration, enable/disable/rebuild/verify trigger, retry batch, retry all batches) to the read-only pages built in Epic 10B.

**Architecture:** Approach B — shared `ConfirmDialog`/`ActionMenu`/`ActionButton` (presentation-only) wired to feature-local `mutations.ts` files (TanStack Query `useMutation`) + Sonner toaster in `providers.tsx`. No CRUD forms. No new backend endpoints.

**Tech Stack:** React 19, TanStack Query v5, Sonner (toast), shadcn AlertDialog + DropdownMenu, AG Grid v35 community

## Global Constraints

- TypeScript strict — no `any`, no implicit `any`
- No AG Grid Enterprise imports
- Relative imports only — no `@/` path alias configured in this project
- 12 existing Vitest tests must remain green
- No new frontend unit tests
- All mutations use `mutateAsync()` — not `mutate()` — so dialog close and toast are sequenced after request completion
- Shared action components (`ConfirmDialog`, `ActionMenu`, `ActionButton`): no React Query imports, no API imports, no toast calls, no business logic — presentation only
- `getErrorMessage()` in `shared/utils/error.ts` is the single error-to-string converter for all features — no per-feature error parsing
- `sonner` added via `npm install sonner`; shadcn `dropdown-menu` and `alert-dialog` added via `npx shadcn@latest add`
- All new API mutation functions return `Promise<void>` (backend returns 204 No Content)
- Column factory functions (`makeLocksColumns`, `makeNodeColumns`, etc.) are exported instead of plain const arrays so action callbacks can be captured via closure
- Working directory for npm/shadcn commands: `src/MSOSync.Frontend/`

---

## Task Overview

| Task | File | Brief |
|------|------|-------|
| 1 | Shared infrastructure | `docs/superpowers/plans/2026-06-30-epic10c-task-1-shared-infra.md` |
| 2 | Locks | `docs/superpowers/plans/2026-06-30-epic10c-task-2-locks.md` |
| 3 | Nodes | `docs/superpowers/plans/2026-06-30-epic10c-task-3-nodes.md` |
| 4 | Triggers | `docs/superpowers/plans/2026-06-30-epic10c-task-4-triggers.md` |
| 5 | Outgoing Batches | `docs/superpowers/plans/2026-06-30-epic10c-task-5-outgoing-batches.md` |

Tasks 2–5 depend on Task 1 (shared components must exist first). Tasks 2–5 are independent of each other.

---

## New Files (11)

| File | Purpose |
|------|---------|
| `shared/utils/error.ts` | `getErrorMessage()` — parse Axios/API errors |
| `shared/components/actions/ConfirmDialog.tsx` | Shadcn AlertDialog wrapper |
| `shared/components/actions/ActionMenu.tsx` | `⋮` DropdownMenu wrapper |
| `shared/components/actions/ActionButton.tsx` | Single-action Button wrapper |
| `shared/components/actions/index.ts` | Barrel re-export |
| `components/ui/dropdown-menu.tsx` | Shadcn generated |
| `components/ui/alert-dialog.tsx` | Shadcn generated |
| `features/locks/mutations.ts` | `useReleaseLockMutation` |
| `features/nodes/mutations.ts` | `useEnableNodeMutation`, `useDisableNodeMutation`, `useApproveRegistrationMutation` |
| `features/triggers/mutations.ts` | `useEnableTriggerMutation`, `useDisableTriggerMutation`, `useRebuildTriggerMutation`, `useVerifyTriggerMutation` |
| `features/outgoing-batches/mutations.ts` | `useRetryBatchMutation`, `useRetryAllBatchesMutation` |

All paths are relative to `src/MSOSync.Frontend/src/`.

---

## Modified Files (10)

| File | Change |
|------|--------|
| `app/providers.tsx` | Add `<Toaster richColors closeButton position="bottom-right" />` |
| `shared/api/locks.ts` | Add `releaseLock(lockName)` |
| `shared/api/nodes.ts` | Add `enableNode`, `disableNode`, `approveRegistration` |
| `shared/api/triggers.ts` | Add `enableTrigger`, `disableTrigger`, `rebuildTrigger`, `verifyTrigger` |
| `shared/api/batches.ts` | Add `retryBatch(batchId)`, `retryAllBatches()` |
| `features/locks/columns.ts` | Replace const with `makeLocksColumns(onRelease)` factory |
| `features/locks/LocksGrid.tsx` | Add confirm state + `useReleaseLockMutation` + `ConfirmDialog` |
| `features/locks/LocksPage.tsx` | Remove placeholder paragraph |
| `features/nodes/columns.ts` | Replace const with `makeNodeColumns(onAction)` factory |
| `features/nodes/NodesGrid.tsx` | Accept `onAction` prop, pass to columns factory |
| `features/nodes/NodesPage.tsx` | Add confirm state + 3 mutations + `ConfirmDialog` |
| `features/triggers/columns.ts` | Replace const with `makeTriggersColumns(onAction, onVerify)` factory |
| `features/triggers/TriggersGrid.tsx` | Accept `onAction` prop + own `useVerifyTriggerMutation` |
| `features/triggers/TriggersPage.tsx` | Add confirm state + 3 mutations + `ConfirmDialog` |
| `features/outgoing-batches/columns.ts` | Replace const with `makeOutgoingBatchColumns(onRetry, pendingBatchId)` factory |
| `features/outgoing-batches/OutgoingBatchesGrid.tsx` | Add `useRetryBatchMutation` + `pendingBatchId` state |
| `features/outgoing-batches/OutgoingBatchesPage.tsx` | Add Retry All button + `useRetryAllBatchesMutation` |

---

## API Route Reference

| Feature | Method | Route |
|---------|--------|-------|
| Locks | DELETE | `/api/v1/locks/{lockName}` |
| Nodes | POST | `/api/v1/nodes/{nodeId}/enable` |
| Nodes | POST | `/api/v1/nodes/{nodeId}/disable` |
| Nodes | POST | `/api/v1/nodes/registrations/{requestId}/approve` |
| Triggers | POST | `/api/v1/triggers/{triggerId}/enable` |
| Triggers | POST | `/api/v1/triggers/{triggerId}/disable` |
| Triggers | POST | `/api/v1/triggers/{triggerId}/rebuild` |
| Triggers | POST | `/api/v1/triggers/{triggerId}/verify` |
| Batches | POST | `/api/v1/outgoing-batches/{batchId}/retry` |
| Batches | POST | `/api/v1/outgoing-batches/retry-all` |

The axios client baseURL already includes `/api/v1/`, so calls omit that prefix.
