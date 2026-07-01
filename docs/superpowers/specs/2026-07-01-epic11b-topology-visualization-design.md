# Epic 11B: Topology Visualization Design

**Goal:** Add a React Flow graph canvas to the Topology page that renders node groups and router connections from the existing `/api/v1/topology/graph` contract, with a right-side detail panel for selected nodes and edges.

**Architecture:** Decomposed components under `features/topology/graph/`. A pure `layoutGraph()` function runs Dagre on every topology data change. React Flow renders immutable node/edge arrays derived from that function. A union selection type drives a single detail panel. Tab state lives in `TopologyPage` and is the single source of truth for both graph and detail panel visibility.

**Tech Stack:** `@xyflow/react` ^12 (already installed), `@dagrejs/dagre` ^1.x (new), `@types/dagre` (new devDep), shadcn Tabs, Tailwind CSS, TanStack Query v5.

---

## Global Constraints

- TypeScript strict, no `any`
- Relative imports only
- No new backend endpoints
- All data from `useTopologyGraph()` hook — no additional API calls
- `@xyflow/react` CSS must be imported: `@xyflow/react/dist/style.css`
- Dagre executes only when topology data changes, never when UI chrome changes (panel open/close, tab switch)
- Graph selection cleared when switching tabs
- Summary cards always visible regardless of active tab

---

## File Map

### New files
- `src/features/topology/graph/constants.ts`
- `src/features/topology/graph/dagre-layout.ts`
- `src/features/topology/graph/TopologyGroupNode.tsx`
- `src/features/topology/graph/TopologyRouterEdge.tsx`
- `src/features/topology/graph/TopologyDetailPanel.tsx`
- `src/features/topology/graph/TopologyGraph.tsx`
- `src/features/topology/graph/index.ts`

### Modified files
- `src/features/topology/TopologyPage.tsx` — add controlled Tabs + mount TopologyGraph
- `src/features/topology/hooks.ts` — add `refetchOnWindowFocus: false` to `useTopologyGraph`

### New dependency
- `package.json`: `@dagrejs/dagre` ^1.x (dep), `@types/dagre` (devDep)

### New tests
- `src/features/topology/graph/dagre-layout.test.ts`

---

## Section 1: Page Structure & Tabs

`TopologyPage` becomes a controlled tab container.

```tsx
// TopologyPage.tsx
const TOPOLOGY_TABS = {
  GRAPH: 'graph',
  GROUPS: 'groups',
} as const;

type TopologyTab = typeof TOPOLOGY_TABS[keyof typeof TOPOLOGY_TABS];

export function TopologyPage() {
  const [selectedTab, setSelectedTab] = useState<TopologyTab>(TOPOLOGY_TABS.GRAPH);

  function handleViewInTable(_groupId: string) {
    setSelectedTab(TOPOLOGY_TABS.GROUPS);
    // Epic 11D/11E: setSelectedGroupId(groupId) for row highlight
  }

  return (
    <div className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Topology</h1>
      </div>
      <TopologySummaryCards />
      <Tabs value={selectedTab} onValueChange={(v) => setSelectedTab(v as TopologyTab)}>
        <TabsList>
          <TabsTrigger value={TOPOLOGY_TABS.GRAPH}>Graph</TabsTrigger>
          <TabsTrigger value={TOPOLOGY_TABS.GROUPS}>Groups</TabsTrigger>
        </TabsList>
        <TabsContent value={TOPOLOGY_TABS.GRAPH}>
          <TopologyGraph onViewInTable={handleViewInTable} />
        </TabsContent>
        <TabsContent value={TOPOLOGY_TABS.GROUPS}>
          <TopologyGroupsGrid />
        </TabsContent>
      </Tabs>
    </div>
  );
}
```

**Invariants:**
- Summary cards are global topology state and must remain visible regardless of active view.
- Tab state is controlled (`value` + `onValueChange`) — never `defaultValue` while also managing state.
- `TopologyGraph` does not know tabs exist. It receives `onViewInTable` as a callback.

---

## Section 2: Constants

```ts
// graph/constants.ts
import { ConnectivityStatus } from '../../../shared/types';

export const GROUP_NODE_WIDTH  = 220;
export const GROUP_NODE_HEIGHT = 100;

export const CONNECTIVITY_META = {
  [ConnectivityStatus.Unknown]: {
    label: 'Unknown',
    dot: 'bg-gray-400',
  },
  [ConnectivityStatus.Reachable]: {
    label: 'Reachable',
    dot: 'bg-green-500',
  },
  [ConnectivityStatus.Degraded]: {
    label: 'Degraded',
    dot: 'bg-amber-400',
  },
  [ConnectivityStatus.Unreachable]: {
    label: 'Unreachable',
    dot: 'bg-red-500',
  },
} as const;
```

All node dimensions and status metadata consumed from this file. No magic numbers in other graph files.

---

## Section 3: Dagre Layout

```ts
// graph/dagre-layout.ts
import dagre from '@dagrejs/dagre';
import type { Node, Edge } from '@xyflow/react';
import type { TopologyGraphNodeDto, TopologyGraphEdgeDto } from '../../../shared/api/topology';
import { GROUP_NODE_WIDTH, GROUP_NODE_HEIGHT } from './constants';

export interface LayoutOptions {
  rankdir?: 'LR' | 'TB';
  nodeWidth?: number;
  nodeHeight?: number;
  nodePadding?: number;
}

export function layoutGraph(
  nodes: TopologyGraphNodeDto[],
  edges: TopologyGraphEdgeDto[],
  options: LayoutOptions = {},
): { nodes: Node[]; edges: Edge[] } {
  const {
    rankdir = 'LR',
    nodeWidth = GROUP_NODE_WIDTH,
    nodeHeight = GROUP_NODE_HEIGHT,
    nodePadding = 48,
  } = options;

  const g = new dagre.graphlib.Graph();
  g.setDefaultEdgeLabel(() => ({}));
  g.setGraph({ rankdir, nodesep: nodePadding, ranksep: nodePadding * 2 });

  for (const node of nodes) {
    g.setNode(node.id, { width: nodeWidth, height: nodeHeight });
  }
  for (const edge of edges) {
    g.setEdge(edge.source, edge.target, { id: edge.id });
  }

  dagre.layout(g);

  const rfNodes: Node[] = nodes.map((node) => {
    const { x, y } = g.node(node.id);
    return {
      id: node.id,
      type: 'groupNode',
      position: { x: x - nodeWidth / 2, y: y - nodeHeight / 2 },
      width: nodeWidth,
      height: nodeHeight,
      data: node,
    };
  });

  const rfEdges: Edge[] = edges.map((edge) => ({
    id: edge.id,
    source: edge.source,
    target: edge.target,
    type: 'routerEdge',
    data: edge,
  }));

  return { nodes: rfNodes, edges: rfEdges };
}
```

**Invariants:**
- Pure function: no React imports, no side effects.
- Default layout direction is LR (left-to-right), matching sync pipeline mental model.
- Node and edge IDs pass through unchanged (`"group:{id}"`, `"router:{id}"`).

---

## Section 4: Custom Node — `TopologyGroupNode`

```tsx
// graph/TopologyGroupNode.tsx
import type { NodeProps } from '@xyflow/react';
import { Handle, Position } from '@xyflow/react';
import type { TopologyGraphNodeDto } from '../../../shared/api/topology';
import { CONNECTIVITY_META } from './constants';

export function TopologyGroupNode({ data, selected }: NodeProps<TopologyGraphNodeDto>) {
  const status = CONNECTIVITY_META[data.status as keyof typeof CONNECTIVITY_META]
    ?? CONNECTIVITY_META[ConnectivityStatus.Unknown]; // fallback to Unknown

  return (
    <div
      role="button"
      aria-label={`Group ${data.label}, ${status.label}`}
      className={`
        w-full h-full rounded-lg border bg-background p-3 text-sm
        flex flex-col gap-1 cursor-pointer transition-shadow
        ${selected ? 'ring-2 ring-primary border-primary' : 'border-border'}
      `}
    >
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <span className={`h-2 w-2 rounded-full ${status.dot}`} />
        <span>{status.label}</span>
      </div>
      <div className="font-semibold truncate">{data.label}</div>
      <div className="text-xs text-muted-foreground">
        {data.memberCount} members · {data.triggerCount} triggers · {data.channelCount} channels
      </div>
      <Handle type="target" position={Position.Left} className="!bg-muted-foreground" />
      <Handle type="source" position={Position.Right} className="!bg-muted-foreground" />
    </div>
  );
}
```

---

## Section 5: Custom Edge — `TopologyRouterEdge`

```tsx
// graph/TopologyRouterEdge.tsx
import {
  BaseEdge,
  EdgeLabelRenderer,
  getBezierPath,
  type EdgeProps,
} from '@xyflow/react';
import type { TopologyGraphEdgeDto } from '../../../shared/api/topology';

export function TopologyRouterEdge({
  id, sourceX, sourceY, targetX, targetY,
  sourcePosition, targetPosition,
  data, selected,
}: EdgeProps<TopologyGraphEdgeDto>) {
  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX, sourceY, sourcePosition,
    targetX, targetY, targetPosition,
  });

  const strokeWidth = selected ? 3 : 2;
  const stroke = selected ? 'var(--primary)' : 'var(--muted-foreground)';
  const strokeDasharray = data?.isEnabled ? undefined : '6 3';

  return (
    <>
      <BaseEdge
        id={id}
        path={edgePath}
        style={{ stroke, strokeWidth, strokeDasharray }}
      />
      <EdgeLabelRenderer>
        <div
          style={{ transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)` }}
          className="absolute text-xs bg-background border border-border rounded px-1 pointer-events-none whitespace-nowrap"
        >
          {data?.channelIds.length ?? 0} ch
        </div>
      </EdgeLabelRenderer>
    </>
  );
}
```

**Invariants:**
- Solid stroke = enabled router; `strokeDasharray="6 3"` = disabled router.
- Channel count badge always rendered (even `1 ch`).
- Labels use `pointer-events-none` so they don't block edge click.

---

## Section 6: Detail Panel — `TopologyDetailPanel`

```tsx
// graph/TopologyDetailPanel.tsx
import type { TopologyGraphNodeDto, TopologyGraphEdgeDto } from '../../../shared/api/topology';
import { CONNECTIVITY_META } from './constants';

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

export function TopologyDetailPanel({ selection, nodeMap, edgeMap, onClose, onViewInTable }: Props) {
  if (!selection) return null;

  if (selection.kind === 'node') {
    const node = nodeMap.get(selection.id);
    if (!node) return null;
    const status = CONNECTIVITY_META[node.status as keyof typeof CONNECTIVITY_META]
      ?? CONNECTIVITY_META[ConnectivityStatus.Unknown];
    return (
      <PanelShell onClose={onClose}>
        <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <span className={`h-2 w-2 rounded-full ${status.dot}`} />
          <span>{status.label}</span>
        </div>
        <h3 className="font-semibold text-base">{node.label}</h3>
        <dl className="text-sm space-y-1 mt-2">
          <Row label="Members" value={node.memberCount} />
          <Row label="Triggers" value={node.triggerCount} />
          <Row label="Channels" value={node.channelCount} />
        </dl>
        <button
          aria-label={`View ${node.label} in groups table`}
          onClick={() => onViewInTable(node.groupId)}
          className="mt-4 text-sm underline text-primary"
        >
          View in Groups Table
        </button>
      </PanelShell>
    );
  }

  if (selection.kind === 'edge') {
    const edge = edgeMap.get(selection.id);
    if (!edge) return null;
    const routerLabel = edge.id.replace(/^router:/, '');
    const sourceLabel = edge.source.replace(/^group:/, '');
    const targetLabel = edge.target.replace(/^group:/, '');
    const sourceGroupId = edge.source.replace(/^group:/, '');
    return (
      <PanelShell onClose={onClose}>
        <h3 className="font-semibold text-base">{sourceLabel} → {targetLabel}</h3>
        <div className="text-xs text-muted-foreground mt-1">Router: {routerLabel}</div>
        <div className={`text-xs mt-2 ${edge.isEnabled ? 'text-green-600' : 'text-muted-foreground'}`}>
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
          aria-label={`View source group ${sourceLabel} in groups table`}
          onClick={() => onViewInTable(sourceGroupId)}
          className="mt-4 text-sm underline text-primary"
        >
          View in Groups Table
        </button>
      </PanelShell>
    );
  }

  return null;
}

function PanelShell({ onClose, children }: { onClose: () => void; children: React.ReactNode }) {
  return (
    <div className="w-72 shrink-0 border-l border-border p-4 flex flex-col">
      <button
        aria-label="Close topology details"
        onClick={onClose}
        className="self-end text-muted-foreground hover:text-foreground mb-2"
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

---

## Section 7: Canvas Orchestrator — `TopologyGraph`

```tsx
// graph/TopologyGraph.tsx
import { useCallback, useMemo } from 'react';
import ReactFlow, { Background, Controls } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { useTopologyGraph } from '../hooks';
import { layoutGraph } from './dagre-layout';
import { TopologyGroupNode } from './TopologyGroupNode';
import { TopologyRouterEdge } from './TopologyRouterEdge';
import { TopologyDetailPanel, type TopologySelection } from './TopologyDetailPanel';
import { useState } from 'react';
import { ErrorState } from '../../../shared/components/feedback/ErrorState';
import { getErrorMessage } from '../../../shared/utils/error';

const EMPTY_GRAPH = { nodes: [], edges: [] } as const;

// Defined at module scope — stable reference, no useMemo needed.
const nodeTypes = { groupNode: TopologyGroupNode };
const edgeTypes = { routerEdge: TopologyRouterEdge };

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
    () => graph.nodes.map((n) => ({ ...n, selected: selection?.kind === 'node' && selection.id === n.id })),
    [graph.nodes, selection],
  );
  const rfEdges = useMemo(
    () => graph.edges.map((e) => ({ ...e, selected: selection?.kind === 'edge' && selection.id === e.id })),
    [graph.edges, selection],
  );

  const handleViewInTable = useCallback((groupId: string) => {
    setSelection(null);
    onViewInTable(groupId);
  }, [onViewInTable]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center" style={{ minHeight: 600 }}>
        <span className="text-muted-foreground text-sm">Loading graph…</span>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center" style={{ minHeight: 600 }}>
        <ErrorState
          title="Failed to load topology graph"
          description={getErrorMessage(error)}
          onRetry={() => void refetch()}
        />
      </div>
    );
  }

  if (data?.nodes.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-1 text-muted-foreground" style={{ minHeight: 600 }}>
        <p className="font-medium">No node groups configured yet.</p>
        <p className="text-sm">Register a node or create a node group to begin synchronization.</p>
      </div>
    );
  }

  return (
    <div className="flex" style={{ minHeight: 600 }}>
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
          <Background />
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

**Invariants:**
- `nodeTypes` / `edgeTypes` defined outside component — stable reference, no `useMemo` needed.
- `nodesDraggable={false}` — read-only for 11B.
- `nodesConnectable={false}` — no edge creation.
- Layout recomputes only when `data` changes (not when `selection` changes).

---

## Section 8: Testing

### `dagre-layout.test.ts` — 6 unit tests

```ts
describe('layoutGraph', () => {
  it('returns empty graph for empty input', () => {
    const result = layoutGraph([], []);
    expect(result.nodes).toHaveLength(0);
    expect(result.edges).toHaveLength(0);
  });

  it('assigns numeric position to a single node', () => {
    const result = layoutGraph([mockNode('g:A')], []);
    expect(result.nodes[0].position).toEqual(
      expect.objectContaining({ x: expect.any(Number), y: expect.any(Number) }),
    );
  });

  it('produces distinct positions for two nodes with an edge', () => {
    const result = layoutGraph(
      [mockNode('g:A'), mockNode('g:B')],
      [mockEdge('r:1', 'g:A', 'g:B')],
    );
    expect(result.nodes[0].position).not.toEqual(result.nodes[1].position);
  });

  it('TB rankdir produces different positions than LR', () => {
    const lr = layoutGraph([mockNode('g:A'), mockNode('g:B')], [mockEdge('r:1', 'g:A', 'g:B')], { rankdir: 'LR' });
    const tb = layoutGraph([mockNode('g:A'), mockNode('g:B')], [mockEdge('r:1', 'g:A', 'g:B')], { rankdir: 'TB' });
    expect(lr.nodes[0].position).not.toEqual(tb.nodes[0].position);
  });

  it('node dimensions from LayoutOptions are reflected in output', () => {
    const result = layoutGraph([mockNode('g:A')], [], { nodeWidth: 300, nodeHeight: 150 });
    expect(result.nodes[0].width).toBe(300);
    expect(result.nodes[0].height).toBe(150);
  });

  it('preserves edge id, source, and target through layout', () => {
    const result = layoutGraph(
      [mockNode('g:A'), mockNode('g:B'), mockNode('g:C')],
      [mockEdge('r:1', 'g:A', 'g:B'), mockEdge('r:2', 'g:A', 'g:C'), mockEdge('r:3', 'g:B', 'g:C')],
    );
    expect(result.edges).toHaveLength(3);
    expect(result.edges.map((e) => e.id)).toEqual(['r:1', 'r:2', 'r:3']);
    expect(result.edges[0].source).toBe('g:A');
    expect(result.edges[0].target).toBe('g:B');
  });
});
```

### Manual acceptance checklist

```
✓ Graph tab opens by default
✓ fitView centers the graph on load
✓ Node click opens detail panel (group info shown)
✓ Edge click opens detail panel (router + channels shown)
✓ Pane click closes detail panel
✓ View in Groups Table switches to Groups tab, panel closes
✓ Disabled routers render as dashed edges
✓ Dark mode renders correctly (theme variables, not hardcoded colors)
✓ Browser resize does not recompute layout
✓ No React Flow warnings in browser console
```

---

## Barrel export

```ts
// graph/index.ts
export { TopologyGraph } from './TopologyGraph';
export type { TopologySelection } from './TopologyDetailPanel';
```
