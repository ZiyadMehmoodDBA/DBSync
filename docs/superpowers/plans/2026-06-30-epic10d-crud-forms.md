# Epic 10D — CRUD Forms Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add create, edit, and delete forms to all six administrative pages (Parameters, Users, Triggers, Channels, Routers, Nodes) using shadcn Dialog + react-hook-form + Zod.

**Architecture:** Feature-local schemas/mutations/dialogs; shared presentation-only form infrastructure in `shared/components/forms/`; single Dialog per entity with `mode: 'create' | 'edit'`; ConfirmDialog for all destructive actions.

**Tech Stack:** React 19, TypeScript strict, react-hook-form, zod, @hookform/resolvers, shadcn Dialog/Form/Input/Select/Checkbox/Textarea, TanStack Query v5, Sonner toasts.

## Global Constraints

- TypeScript strict; no `any`; relative imports only — never `@/` alias in our own code
- `mutateAsync()` everywhere — never `mutate()`
- No optimistic updates — all mutations rely on server ack + query invalidation
- No new Vitest tests — `npm run build` exits 0 + 12/12 existing tests pass = done
- Shared form components (`shared/components/forms/`) are presentation-only: no React Query, no API imports, no toast calls
- All Zod schemas live in `schemas.ts` per feature — not inline in Dialog components
- shadcn Dialog is the only form presentation — no inline grid editing, no Sheet
- All dialogs reset form state and API error on both open and close via `useEffect`
- All destructive actions use `ConfirmDialog` (from 10C, in `shared/components/actions/`) — never inside the form
- `form.formState.isSubmitting` drives Submit button disabled/loading — no custom `isLoading` flags
- Form mutations do NOT include `onError` — Dialog owns error display; 10C operational mutations (with `onError`) are preserved unchanged

---

## File Map

### New files

| File | Task | Purpose |
|---|---|---|
| `src/shared/components/forms/EntityDialog.tsx` | 1 | Dialog wrapper: title, description, scrollable body, max-w-lg |
| `src/shared/components/forms/FormActions.tsx` | 1 | Footer: Cancel + Submit |
| `src/shared/components/forms/FormError.tsx` | 1 | Inline API error callout |
| `src/shared/components/forms/FormSection.tsx` | 1 | Optional visual grouping with heading |
| `src/shared/components/forms/index.ts` | 1 | Barrel export |
| `src/features/parameters/schemas.ts` | 2 | Zod schema + getDefaultValues |
| `src/features/parameters/mutations.ts` | 2 | useUpdateParameterMutation |
| `src/features/parameters/ParameterDialog.tsx` | 2 | Edit-only dialog |
| `src/features/users/schemas.ts` | 3 | Create + update schemas + getDefaultValues |
| `src/features/users/mutations.ts` | 3 | useCreate/Update/DeactivateUserMutation |
| `src/features/users/UserDialog.tsx` | 3 | Create/edit dialog |
| `src/features/channels/schemas.ts` | 4 | Create + update schemas + getDefaultValues |
| `src/features/channels/mutations.ts` | 4 | useCreate/Update/DeleteChannelMutation |
| `src/features/channels/ChannelDialog.tsx` | 4 | Create/edit dialog |
| `src/features/triggers/utils.ts` | 5 | toSourceTable + fromSourceTable helpers |
| `src/features/triggers/schemas.ts` | 5 | Create + update schemas + getDefaultValues |
| `src/features/triggers/TriggerDialog.tsx` | 5 | Create/edit dialog (channelId Select) |
| `src/features/routers/schemas.ts` | 6 | Create + update schemas + ROUTER_TYPES + getDefaultValues |
| `src/features/routers/mutations.ts` | 6 | useCreate/Update/DeleteRouterMutation |
| `src/features/routers/RouterDialog.tsx` | 6 | Create/edit dialog (group Selects) |
| `src/features/nodes/schemas.ts` | 7 | updateNodeSchema + getDefaultValues |
| `src/features/nodes/NodeDialog.tsx` | 7 | Edit-only dialog (groupId Select) |

### Modified files

| File | Task | Change |
|---|---|---|
| `src/shared/api/parameters.ts` | 2 | Add `updateParameter(name, value)` |
| `src/features/parameters/columns.ts` | 2 | `parameterColumns` const → `makeParameterColumns(onEdit)` factory |
| `src/features/parameters/ParametersGrid.tsx` | 2 | Add `onEdit` prop |
| `src/features/parameters/ParametersPage.tsx` | 2 | Add `editState`, render dialog |
| `src/shared/api/users.ts` | 3 | Add `createUser`, `updateUser`, `deactivateUser` |
| `src/features/users/columns.ts` | 3 | `userColumns` const → `makeUserColumns(onEdit, onDeactivate)` |
| `src/features/users/UsersGrid.tsx` | 3 | Add `onEdit`, `onDeactivate` props |
| `src/features/users/UsersPage.tsx` | 3 | Add create/edit/deactivate state + dialogs |
| `src/shared/api/channels.ts` | 4 | Add `createChannel`, `updateChannel`, `deleteChannel` |
| `src/features/channels/columns.ts` | 4 | `channelColumns` const → `makeChannelColumns(onEdit, onDelete)` |
| `src/features/channels/ChannelsGrid.tsx` | 4 | Add `onEdit`, `onDelete` props |
| `src/features/channels/ChannelsPage.tsx` | 4 | Add create/edit/delete state + dialogs |
| `src/shared/api/triggers.ts` | 5 | Add `createTrigger`, `updateTrigger`, `deleteTrigger` |
| `src/features/triggers/mutations.ts` | 5 | Add 3 CRUD hooks (preserve 4 existing 10C hooks) |
| `src/features/triggers/columns.ts` | 5 | Extend `makeTriggersColumns` to accept `onEdit`, `onDelete` |
| `src/features/triggers/TriggersGrid.tsx` | 5 | Add `onEdit`, `onDelete` props |
| `src/features/triggers/TriggersPage.tsx` | 5 | Add create/edit/delete state + dialogs |
| `src/shared/api/routers.ts` | 6 | Add `createRouter`, `updateRouter`, `deleteRouter` |
| `src/features/routers/columns.ts` | 6 | `routerColumns` const → `makeRouterColumns(onEdit, onDelete)` |
| `src/features/routers/RoutersGrid.tsx` | 6 | Add `onEdit`, `onDelete` props |
| `src/features/routers/RoutersPage.tsx` | 6 | Add create/edit/delete state + dialogs |
| `src/shared/api/nodes.ts` | 7 | Add `updateNode` |
| `src/shared/types/nodes.ts` | 7 | Add `syncUrl?: string; heartbeatInterval?: number` to NodeDto |
| `src/features/nodes/mutations.ts` | 7 | Add `useUpdateNodeMutation` |
| `src/features/nodes/columns.ts` | 7 | Extend `makeNodeColumns` to accept `onEdit` |
| `src/features/nodes/NodesGrid.tsx` | 7 | Add `onEdit` prop |
| `src/features/nodes/NodesPage.tsx` | 7 | Add `editState` + NodeDialog |

---

## Tasks

| Task | Description |
|---|---|
| 1 | **Shared infra** — install npm deps, shadcn CLI installs, EntityDialog + FormActions + FormError + FormSection + index.ts |
| 2 | **Parameters** — API fn, schema, mutation, ParameterDialog, columns factory, page wire |
| 3 | **Users** — 3 API fns, 2 schemas, 3 mutations, UserDialog, columns factory, grid + page wire |
| 4 | **Channels** — 3 API fns, 2 schemas, 3 mutations, ChannelDialog, columns factory, grid + page wire |
| 5 | **Triggers** (extend 10C) — 3 API fns, utils.ts, 2 schemas, 3 mutations, TriggerDialog, extend columns factory + grid + page |
| 6 | **Routers** — 3 API fns, 2 schemas, 3 mutations, RouterDialog, columns factory, grid + page wire |
| 7 | **Nodes** (extend 10C) — 1 API fn, type update, 1 schema, 1 mutation, NodeDialog, extend columns factory + grid + page |

Individual task briefs:
- `docs/superpowers/plans/2026-06-30-epic10d-task-1-shared-infra.md`
- `docs/superpowers/plans/2026-06-30-epic10d-task-2-parameters.md`
- `docs/superpowers/plans/2026-06-30-epic10d-task-3-users.md`
- `docs/superpowers/plans/2026-06-30-epic10d-task-4-channels.md`
- `docs/superpowers/plans/2026-06-30-epic10d-task-5-triggers.md`
- `docs/superpowers/plans/2026-06-30-epic10d-task-6-routers.md`
- `docs/superpowers/plans/2026-06-30-epic10d-task-7-nodes.md`

---

## Completion Gates

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
