# Epic 11B Task 3: TopologyDetailPanel + TopologyGraph Orchestrator

## Context

You are implementing the detail panel and the main React Flow canvas orchestrator for Epic 11B. Tasks 1 and 2 are already complete: `constants.ts`, `dagre-layout.ts`, `TopologyGroupNode`, and `TopologyRouterEdge` all exist. This task creates `TopologyDetailPanel.tsx` (the right-side slide-in panel for node/edge details), `TopologyGraph.tsx` (the canvas orchestrator), updates `hooks.ts` to add `refetchOnWindowFocus: false` to `useTopologyGraph`, and creates the barrel `index.ts`.

## Interfaces

**Consumes (from Tasks 1 + 2):**
- `layoutGraph(nodes, edges, options?) → { nodes: Node[], edges: Edge[] }` from `./dagre-layout`
- `TopologyGroupNode` from `./TopologyGroupNode`
- `TopologyRouterEdge` from `./TopologyRouterEdge`
- `CONNECTIVITY_META, ConnectivityStatus` from `./constants`

**Consumes (already in codebase):**
- `useTopologyGraph()` from `../hooks` (returns `{ data, isLoading, error, refetch }`)
  - `data` shape: `{ nodes: TopologyGraphNodeDto[], edges: TopologyGraphEdgeDto[], meta: TopologyGraphMetaDto }`
- `TopologyGraphNodeDto`, `TopologyGraphEdgeDto` from `../../../shared/api/topology`
- `ErrorState` from `../../../shared/components/feedback/ErrorState`
  - Props: `{ error: unknown; onRetry?: () => void }` — NO `title`/`description` props
- `ReactFlow`, `Background`, `Controls` — named exports from `@xyflow/react` (v12 named export)
- `@xyflow/react/dist/style.css` — required CSS import

**Produces:**
- `TopologySelection` type — consumed by `TopologyPage.tsx` in Task 4 (via index.ts)
- `TopologyGraph` component — mounted by `TopologyPage.tsx` in Task 4
  - Props: `{ onViewInTable: (groupId: string) => void }`
- `TopologyDetailPanel` component — internal to `TopologyGraph`

---

## Files

- Create: `src/MSOSync.Frontend/src/features/topology/graph/TopologyDetailPanel.tsx`
- Create: `src/MSOSync.Frontend/src/features/topology/graph/TopologyGraph.tsx`
- Create: `src/MSOSync.Frontend/src/features/topology/graph/index.ts`
- Modify: `src/MSOSync.Frontend/src/features/topology/hooks.ts`

---

- [ ] **Step 1: Update `hooks.ts` — add `refetchOnWindowFocus: false` to `useTopologyGraph`**

Open `src/MSOSync.Frontend/src/features/topology/hooks.ts`. Find the `useTopologyGraph` function and add `refetchOnWindowFocus: false`:

Current:
```ts
export function useTopologyGraph() {
  return useQuery({
    queryKey: queryKeys.topologyGraph(),
    queryFn: getTopologyGraph,
    staleTime: 30_000,
  });
}
```

Updated:
```ts
export function useTopologyGraph() {
  return useQuery({
    queryKey: queryKeys.topologyGraph(),
    queryFn: getTopologyGraph,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 2: Create `TopologyDetailPanel.tsx`**

Create `src/MSOSync.Frontend/src/features/topology/graph/TopologyDetailPanel.tsx`:

```tsx
import type { ReactNode } from 'react';
import type { TopologyGraphNodeDto, TopologyGraphEdgeDto } from '../../../shared/api/topology';
import { CONNECTIVITY_META, ConnectivityStatus } from './constants';

export type TopologySelection =
  | { kind: 'node'; id: string }
  | { kind: 'edge'; id: string }
  | null;

interface Props {
  selection: TopologySelection;
  nodeMap: Map<string, TopologyGraphNodeDto>;
  edgeMap: Map<string, TopologyGraphEdgeDto>;
  onClose: () => void;
  onViewInTable: (groupId: string) => void;
}

export function TopologyDetailPanel({
  selection,
  nodeMap,
  edgeMap,
  onClose,
  onViewInTable,
}: Props) {
  if (!selection) return null;

  if (selection.kind === 'node') {
    const node = nodeMap.get(selection.id);
    if (!node) return null;
    const status = CONNECTIVITY_META[node.status] ?? CONNECTIVITY_META[ConnectivityStatus.Unknown];
    return (
      <PanelShell onClose={onClose}>
        <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <span className={`h-2 w-2 rounded-full shrink-0 ${status.dot}`} />
          <span>{status.label}</span>
        </div>
        <h3 className="font-semibold text-base mt-1">{node.label}</h3>
        <dl className="text-sm space-y-1 mt-3">
          <Row label="Members"  value={node.memberCount}  />
          <Row label="Triggers" value={node.triggerCount} />
          <Row label="Channels" value={node.channelCount} />
        </dl>
        <button
          type="button"
          aria-label={`View ${node.label} in groups table`}
          onClick={() => onViewInTable(node.groupId)}
          className="mt-4 text-sm underline text-primary hover:no-underline"
        >
          View in Groups Table
        </button>
      </PanelShell>
    );
  }

  if (selection.kind === 'edge') {
    const edge = edgeMap.get(selection.id);
    if (!edge) return null;
    const routerLabel   = edge.id.replace(/^router:/, '');
    const sourceLabel   = edge.source.replace(/^group:/, '');
    const targetLabel   = edge.target.replace(/^group:/, '');
    const sourceGroupId = edge.source.replace(/^group:/, '');
    return (
      <PanelShell onClose={onClose}>
        <h3 className="font-semibold text-base">
          {sourceLabel} → {targetLabel}
        </h3>
        <div className="text-xs text-muted-foreground mt-1">Router: {routerLabel}</div>
        <div
          className={[
            'text-xs mt-2',
            edge.isEnabled ? 'text-green-600' : 'text-muted-foreground',
          ].join(' ')}
        >
          {edge.isEnabled ? '● Enabled' : '○ Disabled'}
        </div>
        <div className="mt-3">
          <div className="text-xs font-medium mb-1">Channels</div>
          <ul className="text-sm space-y-0.5">
            {edge.channelIds.map((ch) => (
              <li key={ch} className="text-muted-foreground">• {ch}</li>
            ))}
          </ul>
        </div>
        <button
          type="button"
          aria-label={`View source group ${sourceLabel} in groups table`}
          onClick={() => onViewInTable(sourceGroupId)}
          className="mt-4 text-sm underline text-primary hover:no-underline"
        >
          View in Groups Table
        </button>
      </PanelShell>
    );
  }

  return null;
}

function PanelShell({ onClose, children }: { onClose: () => void; children: ReactNode }) {
  return (
    <div className="w-72 shrink-0 border-l border-border p-4 flex flex-col overflow-y-auto">
      <button
        type="button"
        aria-label="Close topology details"
        onClick={onClose}
        className="self-end text-muted-foreground hover:text-foreground mb-2 leading-none"
      >
        ✕
      </button>
      {children}
    </div>
  );
}

function Row({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex justify-between">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-medium">{value}</dd>
    </div>
  );
}
```

- [ ] **Step 3: Create `TopologyGraph.tsx`**

Create `src/MSOSync.Frontend/src/features/topology/graph/TopologyGraph.tsx`:

```tsx
import { useCallback, useMemo, useState } from 'react';
import { ReactFlow, Background, Controls } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { useTopologyGraph } from '../hooks';
import { layoutGraph } from './dagre-layout';
import { TopologyGroupNode } from './TopologyGroupNode';
import { TopologyRouterEdge } from './TopologyRouterEdge';
import { TopologyDetailPanel, type TopologySelection } from './TopologyDetailPanel';
import { ErrorState } from '../../../shared/components/feedback/ErrorState';

// Defined at module scope — object reference is stable, no useMemo needed.
const nodeTypes = { groupNode: TopologyGroupNode };
const edgeTypes = { routerEdge: TopologyRouterEdge };

const EMPTY_GRAPH = { nodes: [] as never[], edges: [] as never[] };

interface Props {
  onViewInTable: (groupId: string) => void;
}

export function TopologyGraph({ onViewInTable }: Props) {
  const { data, isLoading, error, refetch } = useTopologyGraph();
  const [selection, setSelection] = useState<TopologySelection>(null);

  const graph = useMemo(() => {
    if (!data) return EMPTY_GRAPH;
    return layoutGraph(data.nodes, data.edges);
  }, [data]);

  const nodeMap = useMemo(
    () => new Map((data?.nodes ?? []).map((n) => [n.id, n])),
    [data],
  );
  const edgeMap = useMemo(
    () => new Map((data?.edges ?? []).map((e) => [e.id, e])),
    [data],
  );

  const rfNodes = useMemo(
    () =>
      graph.nodes.map((n) => ({
        ...n,
        selected: selection?.kind === 'node' && selection.id === n.id,
      })),
    [graph.nodes, selection],
  );

  const rfEdges = useMemo(
    () =>
      graph.edges.map((e) => ({
        ...e,
        selected: selection?.kind === 'edge' && selection.id === e.id,
      })),
    [graph.edges, selection],
  );

  const handleViewInTable = useCallback(
    (groupId: string) => {
      setSelection(null);
      onViewInTable(groupId);
    },
    [onViewInTable],
  );

  if (isLoading) {
    return (
      <div className="flex items-center justify-center text-muted-foreground text-sm" style={{ minHeight: 600 }}>
        Loading graph…
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center" style={{ minHeight: 600 }}>
        <ErrorState error={error} onRetry={() => void refetch()} />
      </div>
    );
  }

  if (!data || data.nodes.length === 0) {
    return (
      <div
        className="flex flex-col items-center justify-center gap-1 text-muted-foreground"
        style={{ minHeight: 600 }}
      >
        <p className="font-medium">No node groups configured yet.</p>
        <p className="text-sm">Register a node or create a node group to begin synchronization.</p>
      </div>
    );
  }

  return (
    <div className="flex border border-border rounded-lg overflow-hidden" style={{ minHeight: 600 }}>
      <div className="flex-1">
        <ReactFlow
          nodes={rfNodes}
          edges={rfEdges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          fitView
          fitViewOptions={{ padding: 0.2, duration: 300 }}
          onNodeClick={(_, node) => setSelection({ kind: 'node', id: node.id })}
          onEdgeClick={(_, edge) => setSelection({ kind: 'edge', id: edge.id })}
          onPaneClick={() => setSelection(null)}
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable={false}
        >
          <Background variant="dots" />
          <Controls />
        </ReactFlow>
      </div>
      <TopologyDetailPanel
        selection={selection}
        nodeMap={nodeMap}
        edgeMap={edgeMap}
        onClose={() => setSelection(null)}
        onViewInTable={handleViewInTable}
      />
    </div>
  );
}
```

**Notes:**
- `ReactFlow` is a named export in `@xyflow/react` v12. Use `import { ReactFlow } from '@xyflow/react'`.
- `EMPTY_GRAPH` typed with `never[]` to avoid union type issues when spreading into RF node/edge arrays.
- `ErrorState` props: `{ error, onRetry }` — no `title` or `description`.
- `border border-border rounded-lg overflow-hidden` on the outer div: contains the RF canvas and detail panel inside a visible card boundary.
- `Background variant="dots"` — explicit variant for deterministic rendering.

- [ ] **Step 4: Create `index.ts` barrel**

Create `src/MSOSync.Frontend/src/features/topology/graph/index.ts`:

```ts
export { TopologyGraph } from './TopologyGraph';
export type { TopologySelection } from './TopologyDetailPanel';
```

- [ ] **Step 5: Verify TypeScript compiles**

```pwsh
cd src/MSOSync.Frontend
npx tsc -b --noEmit
```

Expected: no errors. Common issues and fixes:
- If `ReactFlow` import errors: verify `@xyflow/react` export — in v12 it is a named export.
- If `EMPTY_GRAPH` type errors: change to `{ nodes: [], edges: [] }` with explicit type annotation `as { nodes: Node[]; edges: Edge[] }`.
- If `ErrorState` prop errors: its interface is `{ error: unknown; onRetry?: () => void }`.

- [ ] **Step 6: Commit**

```pwsh
cd D:\MSOSync
git add src/MSOSync.Frontend/src/features/topology/hooks.ts
git add src/MSOSync.Frontend/src/features/topology/graph/TopologyDetailPanel.tsx
git add src/MSOSync.Frontend/src/features/topology/graph/TopologyGraph.tsx
git add src/MSOSync.Frontend/src/features/topology/graph/index.ts
git commit -m "feat(11B): add TopologyDetailPanel, TopologyGraph orchestrator, barrel export"
```

## Report Contract

Return: `DONE`, last commit SHA, `tsc --noEmit` result (clean), any concerns. Write full report to the report file path provided by the coordinator.
