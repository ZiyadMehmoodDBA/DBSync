import {
  BaseEdge,
  EdgeLabelRenderer,
  getBezierPath,
  type Edge,
  type EdgeProps,
} from '@xyflow/react';
import type { TopologyGraphEdgeDto } from '../../../shared/api/topology';

type EdgeData = TopologyGraphEdgeDto & Record<string, unknown>;
export type TopologyRouterEdgeType = Edge<EdgeData, 'routerEdge'>;

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
}: EdgeProps<TopologyRouterEdgeType>) {
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
