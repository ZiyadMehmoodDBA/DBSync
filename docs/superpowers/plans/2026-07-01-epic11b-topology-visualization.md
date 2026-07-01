# Epic 11B: Topology Visualization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a React Flow graph canvas to the Topology page showing node groups (nodes) and router connections (edges), with a detail panel for selected elements and a tab/toggle between graph and the existing groups table.

**Architecture:** Pure `layoutGraph()` function converts API DTOs → Dagre-positioned React Flow nodes/edges on every topology data change. `TopologyGraph.tsx` orchestrates loading, error, selection state, and the canvas. `TopologyDetailPanel.tsx` renders node or edge details based on a `TopologySelection` union type. `TopologyPage.tsx` owns tab state (controlled `<Tabs>`), mounts `TopologyGraph` on the graph tab, and passes `onViewInTable(groupId)` to switch tabs.

**Tech Stack:** `@xyflow/react` ^12 (already installed), `@dagrejs/dagre` ^1.x (new), `@types/dagre` (new devDep), shadcn Tabs (new — `npx shadcn add tabs`), Tailwind CSS, Vitest.

## Global Constraints

- TypeScript strict, no `any`, `TreatWarningsAsErrors` equivalent enforced by `tsc -b`
- Relative imports only — no `@/` aliases
- No new backend endpoints; all data via `useTopologyGraph()` hook
- `@xyflow/react` CSS import required: `import '@xyflow/react/dist/style.css'`
- `layoutGraph()` executes only when topology API data changes — never on selection or tab changes
- `nodeTypes` and `edgeTypes` must be defined at module scope (not inside a component) to maintain stable object references
- `elementsSelectable={false}` on `<ReactFlow>` — selection state is owned by application, not React Flow internals
- Graph selection must be cleared when switching tabs
- Summary cards (`<TopologySummaryCards />`) stay outside the tab panel and are always visible
- `ConnectivityStatus` enum does not exist in frontend shared types — define it in `constants.ts`
- `ErrorState` component props are `{ error: unknown; onRetry?: () => void }` — no `title`/`description`
- `ReactFlow` is a named export in `@xyflow/react` v12: `import { ReactFlow } from '@xyflow/react'`

---

## File Map

| File | Task | Action |
|------|------|--------|
| `src/MSOSync.Frontend/src/features/topology/graph/constants.ts` | 1 | Create |
| `src/MSOSync.Frontend/src/features/topology/graph/dagre-layout.ts` | 1 | Create |
| `src/MSOSync.Frontend/src/features/topology/graph/dagre-layout.test.ts` | 1 | Create |
| `src/MSOSync.Frontend/src/features/topology/graph/TopologyGroupNode.tsx` | 2 | Create |
| `src/MSOSync.Frontend/src/features/topology/graph/TopologyRouterEdge.tsx` | 2 | Create |
| `src/MSOSync.Frontend/src/features/topology/graph/TopologyDetailPanel.tsx` | 3 | Create |
| `src/MSOSync.Frontend/src/features/topology/graph/TopologyGraph.tsx` | 3 | Create |
| `src/MSOSync.Frontend/src/features/topology/graph/index.ts` | 3 | Create |
| `src/MSOSync.Frontend/src/features/topology/hooks.ts` | 3 | Modify |
| `src/MSOSync.Frontend/src/features/topology/TopologyPage.tsx` | 4 | Modify |
| `src/MSOSync.Frontend/src/components/ui/tabs.tsx` | 4 | Install (shadcn) |
| `src/MSOSync.Frontend/package.json` | 1 | Modify (add deps) |

---

## Task Files

- Task 1: `2026-07-01-epic11b-task-1-deps-constants-layout.md`
- Task 2: `2026-07-01-epic11b-task-2-node-edge-types.md`
- Task 3: `2026-07-01-epic11b-task-3-detail-panel-graph-orchestrator.md`
- Task 4: `2026-07-01-epic11b-task-4-page-tabs-build.md`

---

## Manual Acceptance Checklist (run after Task 4)

```
✓ Graph tab opens by default
✓ fitView centers graph on load
✓ Node click opens detail panel (label, status, member/trigger/channel counts)
✓ Edge click opens detail panel (router id, enabled/disabled, channel list)
✓ Pane click closes detail panel
✓ "View in Groups Table" switches to Groups tab, clears graph selection
✓ Disabled routers render as dashed edges
✓ Dark mode renders correctly (no hardcoded colors)
✓ Browser resize does not recompute layout
✓ No React Flow warnings in browser console
```
