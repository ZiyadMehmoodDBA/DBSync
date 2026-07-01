# Epic 11B Task 1: Deps + Constants + Dagre Layout + Unit Tests

## Context

You are implementing the foundation of Epic 11B: Topology Visualization. This task installs the new dependency (`@dagrejs/dagre`), creates the `constants.ts` file (node dimensions + connectivity status metadata), implements the pure `layoutGraph()` function, and writes 6 unit tests that verify its behavior. No React components are created in this task.

## Interfaces

**Consumes:** Nothing from earlier tasks (this is Task 1).

**Produces:**
- `GROUP_NODE_WIDTH = 220` and `GROUP_NODE_HEIGHT = 100` from `constants.ts`
- `ConnectivityStatus` const object from `constants.ts`
- `CONNECTIVITY_META` record from `constants.ts`
- `layoutGraph(nodes, edges, options?) → { nodes: Node[], edges: Edge[] }` from `dagre-layout.ts`
- `LayoutOptions` interface from `dagre-layout.ts`

---

## Files

- Create: `src/MSOSync.Frontend/src/features/topology/graph/constants.ts`
- Create: `src/MSOSync.Frontend/src/features/topology/graph/dagre-layout.ts`
- Create: `src/MSOSync.Frontend/src/features/topology/graph/dagre-layout.test.ts`
- Modify: `src/MSOSync.Frontend/package.json` (add `@dagrejs/dagre` dep + `@types/dagre` devDep)

---

- [ ] **Step 1: Install dependencies**

Run from `src/MSOSync.Frontend/`:

```pwsh
cd src/MSOSync.Frontend
npm install @dagrejs/dagre@^1
npm install --save-dev @types/dagre
```

Expected: both packages appear in `package.json` and `node_modules`.

- [ ] **Step 2: Create `constants.ts`**

Create `src/MSOSync.Frontend/src/features/topology/graph/constants.ts`:

```ts
export const GROUP_NODE_WIDTH  = 220;
export const GROUP_NODE_HEIGHT = 100;

export const ConnectivityStatus = {
  Unknown:     0,
  Reachable:   1,
  Degraded:    2,
  Unreachable: 3,
} as const;

export type ConnectivityStatusValue =
  typeof ConnectivityStatus[keyof typeof ConnectivityStatus];

export const CONNECTIVITY_META: Record<number, { label: string; dot: string }> = {
  [ConnectivityStatus.Unknown]:     { label: 'Unknown',     dot: 'bg-gray-400'  },
  [ConnectivityStatus.Reachable]:   { label: 'Reachable',   dot: 'bg-green-500' },
  [ConnectivityStatus.Degraded]:    { label: 'Degraded',    dot: 'bg-amber-400' },
  [ConnectivityStatus.Unreachable]: { label: 'Unreachable', dot: 'bg-red-500'   },
};
```

- [ ] **Step 3: Write the 6 failing tests**

Create `src/MSOSync.Frontend/src/features/topology/graph/dagre-layout.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { layoutGraph } from './dagre-layout';
import type { TopologyGraphNodeDto, TopologyGraphEdgeDto } from '../../../shared/api/topology';

function mockNode(id: string): TopologyGraphNodeDto {
  return {
    id,
    groupId: id.replace('g:', ''),
    label: id,
    status: 1,
    memberCount: 1,
    triggerCount: 0,
    channelCount: 0,
  };
}

function mockEdge(id: string, source: string, target: string): TopologyGraphEdgeDto {
  return { id, source, target, channelIds: [], isEnabled: true };
}

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
    const shared = {
      nodes: [mockNode('g:A'), mockNode('g:B')],
      edges: [mockEdge('r:1', 'g:A', 'g:B')],
    };
    const lr = layoutGraph(shared.nodes, shared.edges, { rankdir: 'LR' });
    const tb = layoutGraph(shared.nodes, shared.edges, { rankdir: 'TB' });
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

- [ ] **Step 4: Run tests to verify they fail**

```pwsh
cd src/MSOSync.Frontend
npx vitest run src/features/topology/graph/dagre-layout.test.ts
```

Expected: all 6 tests fail with "Cannot find module './dagre-layout'" or similar.

- [ ] **Step 5: Create `dagre-layout.ts`**

Create `src/MSOSync.Frontend/src/features/topology/graph/dagre-layout.ts`:

```ts
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
    rankdir    = 'LR',
    nodeWidth  = GROUP_NODE_WIDTH,
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
      id:       node.id,
      type:     'groupNode',
      position: { x: x - nodeWidth / 2, y: y - nodeHeight / 2 },
      width:    nodeWidth,
      height:   nodeHeight,
      data:     node,
    };
  });

  const rfEdges: Edge[] = edges.map((edge) => ({
    id:     edge.id,
    source: edge.source,
    target: edge.target,
    type:   'routerEdge',
    data:   edge,
  }));

  return { nodes: rfNodes, edges: rfEdges };
}
```

- [ ] **Step 6: Run tests to verify they pass**

```pwsh
cd src/MSOSync.Frontend
npx vitest run src/features/topology/graph/dagre-layout.test.ts
```

Expected: all 6 tests pass.

- [ ] **Step 7: Commit**

```pwsh
cd D:\MSOSync
git add src/MSOSync.Frontend/package.json src/MSOSync.Frontend/package-lock.json
git add src/MSOSync.Frontend/src/features/topology/graph/constants.ts
git add src/MSOSync.Frontend/src/features/topology/graph/dagre-layout.ts
git add src/MSOSync.Frontend/src/features/topology/graph/dagre-layout.test.ts
git commit -m "feat(11B): add dagre layout + constants + 6 unit tests"
```

## Report Contract

Return: `DONE`, last commit SHA, test output (all 6 pass), any concerns. Write full report to the report file path provided by the coordinator.
