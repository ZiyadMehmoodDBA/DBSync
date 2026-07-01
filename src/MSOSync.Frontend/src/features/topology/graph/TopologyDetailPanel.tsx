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
