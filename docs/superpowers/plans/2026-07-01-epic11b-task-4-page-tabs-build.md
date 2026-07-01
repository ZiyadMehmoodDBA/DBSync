# Epic 11B Task 4: TopologyPage Tabs + Full Build Green

## Context

You are wiring everything together for Epic 11B. Tasks 1–3 are complete: `constants.ts`, `dagre-layout.ts` (6 tests passing), `TopologyGroupNode`, `TopologyRouterEdge`, `TopologyDetailPanel`, `TopologyGraph`, `index.ts`, and `hooks.ts` update all exist. This task installs the shadcn Tabs component (not yet in the project), rewrites `TopologyPage.tsx` to use controlled tabs, verifies the full `npm run build` is green, and runs the full Vitest suite.

## Interfaces

**Consumes (from Task 3):**
- `TopologyGraph` from `./graph` (via barrel `index.ts`)
  - Props: `{ onViewInTable: (groupId: string) => void }`

**Consumes (already in codebase):**
- `TopologySummaryCards` from `./TopologySummaryCards`
- `TopologyGroupsGrid` from `./TopologyGroupsGrid`
- `Tabs`, `TabsList`, `TabsTrigger`, `TabsContent` from `../../components/ui/tabs`
  - **Not yet installed** — install in Step 1

**Produces:**
- Updated `TopologyPage.tsx` with controlled Graph | Groups tabs
- Full `npm run build` exit 0
- Full `npm run test` (Vitest) all green

---

## Files

- Modify: `src/MSOSync.Frontend/src/features/topology/TopologyPage.tsx`
- Create: `src/MSOSync.Frontend/src/components/ui/tabs.tsx` (via shadcn)

---

- [ ] **Step 1: Install shadcn Tabs component**

```pwsh
cd src/MSOSync.Frontend
npx shadcn add tabs
```

Expected: creates `src/components/ui/tabs.tsx`. If prompted to overwrite other files, say no to all except tabs.

Verify the file exists:
```pwsh
ls src/components/ui/tabs.tsx
```

- [ ] **Step 2: Rewrite `TopologyPage.tsx`**

Replace the entire contents of `src/MSOSync.Frontend/src/features/topology/TopologyPage.tsx`:

```tsx
import { useState } from 'react';
import {
  Tabs,
  TabsList,
  TabsTrigger,
  TabsContent,
} from '../../components/ui/tabs';
import { TopologySummaryCards } from './TopologySummaryCards';
import { TopologyGroupsGrid } from './TopologyGroupsGrid';
import { TopologyGraph } from './graph';

const TOPOLOGY_TABS = {
  GRAPH:  'graph',
  GROUPS: 'groups',
} as const;

type TopologyTab = typeof TOPOLOGY_TABS[keyof typeof TOPOLOGY_TABS];

export function TopologyPage() {
  const [selectedTab, setSelectedTab] = useState<TopologyTab>(TOPOLOGY_TABS.GRAPH);

  function handleViewInTable(_groupId: string) {
    setSelectedTab(TOPOLOGY_TABS.GROUPS);
    // Epic 11D/11E: pass groupId to scroll/highlight row in groups table
  }

  return (
    <div className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Topology</h1>
        <p className="text-sm text-neutral-500 dark:text-neutral-400 mt-1">
          Network topology of node groups and router connections.
        </p>
      </div>

      {/* Summary cards are global topology state — always visible, outside tab boundary */}
      <TopologySummaryCards />

      <Tabs
        value={selectedTab}
        onValueChange={(v) => setSelectedTab(v as TopologyTab)}
      >
        <TabsList>
          <TabsTrigger value={TOPOLOGY_TABS.GRAPH}>Graph</TabsTrigger>
          <TabsTrigger value={TOPOLOGY_TABS.GROUPS}>Groups</TabsTrigger>
        </TabsList>

        <TabsContent value={TOPOLOGY_TABS.GRAPH}>
          <TopologyGraph onViewInTable={handleViewInTable} />
        </TabsContent>

        <TabsContent value={TOPOLOGY_TABS.GROUPS}>
          <div>
            <h2 className="text-base font-semibold mb-3">Node Groups</h2>
            <TopologyGroupsGrid />
          </div>
        </TabsContent>
      </Tabs>
    </div>
  );
}
```

**Notes:**
- `_groupId` parameter with leading underscore satisfies TypeScript's no-unused-variable rule while preserving the correct callback signature for future use.
- Tab state is controlled (`value` + `onValueChange`) — no `defaultValue`.
- `TopologyGraph` does not know tabs exist; it only calls `onViewInTable(groupId)`.
- Summary cards are outside the `<Tabs>` element intentionally.

- [ ] **Step 3: Run TypeScript type check**

```pwsh
cd src/MSOSync.Frontend
npx tsc -b --noEmit
```

Expected: no errors. If you see errors about missing tabs imports, verify Step 1 succeeded and the file is at `src/components/ui/tabs.tsx`. If `TopologyGraph` import fails, check the barrel at `src/features/topology/graph/index.ts` exports `{ TopologyGraph }`.

- [ ] **Step 4: Run all Vitest tests**

```pwsh
cd src/MSOSync.Frontend
npm run test
```

Expected: all tests pass including the 6 `dagre-layout` tests. No regressions in other test files.

- [ ] **Step 5: Run production build**

```pwsh
cd src/MSOSync.Frontend
npm run build
```

Expected: exit 0, no errors. Common issues:
- If `@xyflow/react/dist/style.css` import errors in build: verify the package version supports this path (`@xyflow/react` ^12 does).
- If shadcn Tabs import fails: verify the file was installed at `src/components/ui/tabs.tsx` and the import path in `TopologyPage.tsx` uses `../../components/ui/tabs`.

- [ ] **Step 6: Manual smoke test in browser**

Start the dev server if not already running:
```pwsh
cd src/MSOSync.Frontend
npm run dev
```

Open the browser and navigate to the Topology page. Verify the manual acceptance checklist:

```
✓ Graph tab is active by default
✓ Summary cards visible above tabs
✓ React Flow canvas renders (or shows empty state if no data)
✓ Node click → right panel opens with group details
✓ Edge click → right panel opens with router details
✓ Pane click → panel closes
✓ "View in Groups Table" button → switches to Groups tab, panel closes
✓ Groups tab still shows the AG Grid table unchanged
✓ No React Flow warnings in browser console (F12 → Console)
```

- [ ] **Step 7: Commit**

```pwsh
cd D:\MSOSync
git add src/MSOSync.Frontend/src/components/ui/tabs.tsx
git add src/MSOSync.Frontend/src/features/topology/TopologyPage.tsx
git commit -m "feat(11B): wire TopologyPage tabs, graph default view, groups table secondary"
```

## Report Contract

Return: `DONE`, last commit SHA, build result (exit 0), test results (all pass, count), manual checklist items verified, any concerns. Write full report to the report file path provided by the coordinator.
