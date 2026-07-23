# Phase 2C.3 — Marketplace UI + Auto-update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a searchable plugin marketplace page at `/marketplace` and an updates panel on the existing plugins page, with nav badge and notification bell integration for available updates.

**Architecture:** A pure frontend feature layered over the existing `shared/api → shared/hooks → features/` pattern. All types live in `shared/types/marketplace.ts`, all API functions in `shared/api/marketplace.ts`, all TanStack Query hooks in `shared/hooks/useMarketplace.ts`, and all components in `features/plugins/`. The `AppLayout` and `NotificationBell` are wired to a lightweight `useUpdateCount` hook that reads from the shared TanStack Query cache — no additional network calls.

**Tech Stack:** React 19, TypeScript, TanStack Query v5, React Router v6, lucide-react, shadcn/ui (Sheet, Select, Badge, Button, Separator), sonner (toasts), Vitest + React Testing Library

## Global Constraints

- Admin-only: `/marketplace` wrapped in `<PermissionGuard permissionKey={PermissionKeys.ManagePlugins}>` — no page-level role check inside `MarketplacePage`
- No lazy imports in router — eager import only, matching all other pages in `router.tsx`
- Toast library: `sonner` only — `import { toast } from 'sonner'`
- `client` import: `import client from './client'` (relative from `shared/api/marketplace.ts`) — never import axios directly
- 503 from backend = "Marketplace not configured" empty state — never `<ErrorState>`
- `useUpdateCount` returns `0` silently on 503, network errors, or any failure — never throws
- Search debounce: 300 ms — `useEffect` + `setTimeout` / `clearTimeout` — no query on every keystroke
- Sort: client-side only — `newest` by `updatedAt` desc, `popular` by `downloadCount` desc, `rating` by `rating` desc — no `sort` query param sent to backend
- Default `pageSize: 20` — user cannot change in v1
- "Update All": sequential `for...of` loop with `await mutateAsync(...)` — not `Promise.all`
- `staleTime` discipline: search 60 s, plugin detail 120 s, updates/updateCount 300 s
- `isInstalled` prop computed in `MarketplacePage` by checking `plugin.id` against installed IDs from `usePlugins()` cache — no extra API call
- Icon image errors: `<img onError>` hides image and shows `Package` fallback
- All marketplace components in `src/features/plugins/` — nothing new under `src/shared/components/`
- Reuse existing `EmptyState`, `ErrorState`, `Button`, `Sheet`, `Select`, `Badge`, `Separator` from shadcn
- Query invalidation on install: `queryKeys.plugins.all()`
- Query invalidation on update: `queryKeys.plugins.all()`, `queryKeys.marketplace.updates()`, `queryKeys.marketplace.updateCount()`
- Route path: `marketplace` (not `administration/marketplace`) — matches spec
- Accessibility: all interactive elements have `aria-label` as specified in constraints

---

## File Map

### New Files
| File | Responsibility |
|---|---|
| `src/MSOSync.Frontend/src/shared/types/marketplace.ts` | All TS interfaces and constants matching backend DTOs |
| `src/MSOSync.Frontend/src/shared/api/marketplace.ts` | Raw API functions using `client` |
| `src/MSOSync.Frontend/src/shared/hooks/useMarketplace.ts` | All TanStack Query hooks + mutations |
| `src/MSOSync.Frontend/src/features/plugins/MarketplaceStarRating.tsx` | Read-only 5-star display |
| `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginCard.tsx` | Grid card: icon, name, author, rating, install btn |
| `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginCard.test.tsx` | Card component tests |
| `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginDrawer.tsx` | shadcn Sheet: detail, version selector, install |
| `src/MSOSync.Frontend/src/features/plugins/MarketplacePage.tsx` | `/marketplace` route: search, filter, sort, grid, pagination |
| `src/MSOSync.Frontend/src/features/plugins/MarketplacePage.test.tsx` | Page-level component tests |
| `src/MSOSync.Frontend/src/features/plugins/UpdatesPanel.tsx` | Updates section on PluginsPage |
| `src/MSOSync.Frontend/src/features/plugins/UpdatesPanel.test.tsx` | UpdatesPanel component tests |

### Modified Files
| File | Change |
|---|---|
| `src/MSOSync.Frontend/src/shared/queryKeys.ts` | Add `marketplace` key group |
| `src/MSOSync.Frontend/src/app/router.tsx` | Add `/marketplace` route with eager import |
| `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` | Add `Store` nav item + `updateCount` badge; import `useUpdateCount` |
| `src/MSOSync.Frontend/src/features/notifications/NotificationBell.tsx` | Add plugin update banner above notification list |
| `src/MSOSync.Frontend/src/features/plugins/PluginsPage.tsx` | Add `<UpdatesPanel>` below `<PluginSummaryCard>` |

---

## Task Index

| # | Name | Deliverable |
|---|---|---|
| 1 | Types + API layer | `marketplace.ts` types, `marketplace.ts` API, `queryKeys.ts` extension |
| 2 | Hooks | `useMarketplace.ts` with all 6 exported hooks |
| 3 | MarketplaceStarRating + MarketplacePluginCard | Two leaf components + card tests |
| 4 | MarketplacePluginDrawer + MarketplacePage | Full page + drawer, router + nav wiring |
| 5 | UpdatesPanel + notification bell integration | UpdatesPanel, PluginsPage wiring, NotificationBell banner, tests |

---

## Dependency Order

```
Task 1 (types + API + queryKeys)
  └── Task 2 (hooks — imports API + queryKeys)
        ├── Task 3 (card components — no hooks dependency; imports types only)
        └── Task 4 (MarketplacePage — imports hooks + card components)
              └── Task 5 (UpdatesPanel + wiring — imports hooks; tests for tasks 4 + 5)
```

Tasks 3 and 4 depend on Task 2. Task 3 (card) can start in parallel with Task 4 up to the point where `MarketplacePage` assembles the cards — but since they are typically done sequentially, treat Task 3 → Task 4 → Task 5 as the order.
