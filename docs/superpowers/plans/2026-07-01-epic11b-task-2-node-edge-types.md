# Epic 11B Task 2: TopologyGroupNode + TopologyRouterEdge

## Context

You are implementing the custom React Flow node and edge types for Epic 11B. Task 1 already created `constants.ts` (with `CONNECTIVITY_META`, `GROUP_NODE_WIDTH/HEIGHT`, `ConnectivityStatus`) and `dagre-layout.ts`. This task creates the two React components that React Flow uses to render group nodes and router edges. No tests in this task — these are visual presentation components wired in Task 3.

## Interfaces

**Consumes (from Task 1):**
- `CONNECTIVITY_META: Record<number, { label: string; dot: string }>` from `./constants`
- `ConnectivityStatus` const object from `./constants`

**Consumes (already in codebase):**
- `TopologyGraphNodeDto` from `../../../shared/api/topology`
- `TopologyGraphEdgeDto` from `../../../shared/api/topology`
- `@xyflow/react`: `NodeProps`, `Handle`, `Position`, `EdgeProps`, `BaseEdge`, `EdgeLabelRenderer`, `getBezierPath`

**Produces:**
- `TopologyGroupNode` component — registered as React Flow node type `"groupNode"`
- `TopologyRouterEdge` component — registered as React Flow edge type `"routerEdge"`

Both consumed in Task 3 by `TopologyGraph.tsx`.

---

## Files

- Create: `src/MSOSync.Frontend/src/features/topology/graph/TopologyGroupNode.tsx`
- Create: `src/MSOSync.Frontend/src/features/topology/graph/TopologyRouterEdge.tsx`

---

- [ ] **Step 1: Create `TopologyGroupNode.tsx`**

Create `src/MSOSync.Frontend/src/features/topology/graph/TopologyGroupNode.tsx`:

```tsx
import type { NodeProps } from '@xyflow/react';
import { Handle, Position } from '@xyflow/react';
import type { TopologyGraphNodeDto } from '../../../shared/api/topology';
import { CONNECTIVITY_META, ConnectivityStatus } from './constants';

export function TopologyGroupNode({ data, selected }: NodeProps<TopologyGraphNodeDto>) {
  const status = CONNECTIVITY_META[data.status] ?? CONNECTIVITY_META[ConnectivityStatus.Unknown];

  return (
    <div
      role="button"
      aria-label={`Group ${data.label}, ${status.label}`}
      className={[
        'w-full h-full rounded-lg border bg-background p-3 text-sm',
        'flex flex-col gap-1 cursor-pointer transition-shadow',
        selected ? 'ring-2 ring-primary border-primary' : 'border-border',
      ].join(' ')}
    >
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <span className={`h-2 w-2 rounded-full shrink-0 ${status.dot}`} />
        <span>{status.label}</span>
      </div>
      <div className="font-semibold truncate">{data.label}</div>
      <div className="text-xs text-muted-foreground">
        {data.memberCount} members · {data.triggerCount} triggers · {data.channelCount} channels
      </div>
      <Handle type="target" position={Position.Left}  className="!bg-muted-foreground" />
      <Handle type="source" position={Position.Right} className="!bg-muted-foreground" />
    </div>
  );
}
```

**Notes:**
- `data` is the `TopologyGraphNodeDto` passed via the React Flow node's `data` field (set in `dagre-layout.ts`).
- `selected` is set externally in `TopologyGraph.tsx` by spreading `{ selected: selection?.id === n.id }` onto each RF node — NOT from RF's internal selection system.
- Use array-join for className to avoid TS template-literal warnings with conditional classes.
- `shrink-0` on the status dot prevents it from collapsing if label is long.

- [ ] **Step 2: Create `TopologyRouterEdge.tsx`**

Create `src/MSOSync.Frontend/src/features/topology/graph/TopologyRouterEdge.tsx`:

```tsx
import {
  BaseEdge,
  EdgeLabelRenderer,
  getBezierPath,
  type EdgeProps,
} from '@xyflow/react';
import type { TopologyGraphEdgeDto } from '../../../shared/api/topology';

export function TopologyRouterEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  data,
  selected,
}: EdgeProps<TopologyGraphEdgeDto>) {
  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  });

  const strokeWidth    = selected ? 3 : 2;
  const stroke         = selected ? 'var(--color-primary)' : 'var(--color-muted-foreground)';
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
          style={{
            transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)`,
          }}
          className="absolute text-xs bg-background border border-border rounded px-1 pointer-events-none whitespace-nowrap"
        >
          {data?.channelIds.length ?? 0} ch
        </div>
      </EdgeLabelRenderer>
    </>
  );
}
```

**Notes:**
- Solid stroke = enabled router; `strokeDasharray="6 3"` = disabled router.
- Channel count badge always rendered (`1 ch`, `2 ch`, etc.) — never suppressed.
- `pointer-events-none` on the label div ensures it doesn't intercept edge clicks.
- `whitespace-nowrap` prevents badge text wrapping.
- CSS variables `var(--color-primary)` and `var(--color-muted-foreground)` — Tailwind v4 uses `--color-*` prefix. If the project uses a different variable convention, check `tailwind.config` or existing theme usage. Fallback: use Tailwind class strings via inline conditional if CSS vars don't resolve correctly.
- `selected` is set externally by `TopologyGraph.tsx` (same pattern as nodes — RF internal selection is disabled via `elementsSelectable={false}`).

- [ ] **Step 3: Verify TypeScript compiles**

```pwsh
cd src/MSOSync.Frontend
npx tsc -b --noEmit
```

Expected: no errors. If you see errors about `NodeProps<TopologyGraphNodeDto>` or `EdgeProps<TopologyGraphEdgeDto>`, verify that `TopologyGraphNodeDto` and `TopologyGraphEdgeDto` are exported from `../../../shared/api/topology`. They were added in Epic 11A — the file is at `src/shared/api/topology.ts`.

- [ ] **Step 4: Commit**

```pwsh
cd D:\MSOSync
git add src/MSOSync.Frontend/src/features/topology/graph/TopologyGroupNode.tsx
git add src/MSOSync.Frontend/src/features/topology/graph/TopologyRouterEdge.tsx
git commit -m "feat(11B): add TopologyGroupNode and TopologyRouterEdge custom RF types"
```

## Report Contract

Return: `DONE`, last commit SHA, `tsc --noEmit` result (clean), any concerns. Write full report to the report file path provided by the coordinator.
