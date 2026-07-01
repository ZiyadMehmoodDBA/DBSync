import { useCallback, useMemo, useState } from 'react';
import { ReactFlow, Background, BackgroundVariant, Controls } from '@xyflow/react';
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
          <Background variant={BackgroundVariant.Dots} />
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
