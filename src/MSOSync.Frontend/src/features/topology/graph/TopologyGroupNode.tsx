import type { Node, NodeProps } from '@xyflow/react';
import { Handle, Position } from '@xyflow/react';
import type { TopologyGraphNodeDto } from '../../../shared/api/topology';
import { CONNECTIVITY_META, LIFECYCLE_META, ConnectivityStatus } from './constants';

type NodeData = TopologyGraphNodeDto & Record<string, unknown>;
export type TopologyGroupNodeType = Node<NodeData, 'groupNode'>;

export function TopologyGroupNode({ data, selected }: NodeProps<TopologyGroupNodeType>) {
  const connectivity = CONNECTIVITY_META[data.status] ?? CONNECTIVITY_META[ConnectivityStatus.Unknown];
  const lifecycle = data.lifecycleState
    ? (LIFECYCLE_META[data.lifecycleState] ?? null)
    : null;

  // Border: lifecycle state wins over selection ring; selection adds ring
  const lifecycleBorder = lifecycle?.border ?? 'border-border';

  return (
    <div
      role="button"
      aria-label={`Group ${data.label}, ${connectivity.label}${lifecycle ? `, ${lifecycle.label}` : ''}`}
      className={[
        'w-full h-full rounded-lg border bg-background p-3 text-sm',
        'flex flex-col gap-1 cursor-pointer transition-shadow',
        selected ? `ring-2 ring-primary ${lifecycleBorder}` : lifecycleBorder,
      ].join(' ')}
    >
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <span className={`h-2 w-2 rounded-full shrink-0 ${connectivity.dot}`} />
        <span>{connectivity.label}</span>
        {lifecycle && (
          <>
            <span aria-hidden className="ml-1">{lifecycle.icon}</span>
            <span>{lifecycle.label}</span>
          </>
        )}
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
