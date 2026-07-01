import dagre from '@dagrejs/dagre';
import type { Node, Edge } from '@xyflow/react';
import type { TopologyGraphNodeDto, TopologyGraphEdgeDto } from '../../../shared/api/topology';
import { GROUP_NODE_WIDTH, GROUP_NODE_HEIGHT } from './constants';

// @xyflow/react requires data to extend Record<string, unknown>.
// The DTO interfaces satisfy the structural constraint at runtime but lack
// an index signature, so we widen them here for type compatibility.
type NodeData = TopologyGraphNodeDto & Record<string, unknown>;
type EdgeData = TopologyGraphEdgeDto & Record<string, unknown>;
type TopologyNode = Node<NodeData>;
type TopologyEdge = Edge<EdgeData>;

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
): { nodes: TopologyNode[]; edges: TopologyEdge[] } {
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

  // Compute raw top-left positions for each node
  const rawPositions = nodes.map((node) => {
    const { x, y } = g.node(node.id);
    return { id: node.id, x: x - nodeWidth / 2, y: y - nodeHeight / 2 };
  });

  // Center the graph so the bounding-box midpoint is at origin.
  // This ensures LR and TB produce visually distinct coordinates for every node.
  const xs = rawPositions.map((p) => p.x);
  const ys = rawPositions.map((p) => p.y);
  const minX = Math.min(...xs);
  const maxX = Math.max(...xs) + nodeWidth;
  const minY = Math.min(...ys);
  const maxY = Math.max(...ys) + nodeHeight;
  const cx = (minX + maxX) / 2;
  const cy = (minY + maxY) / 2;

  const rfNodes: TopologyNode[] = nodes.map((node, i) => ({
    id:       node.id,
    type:     'groupNode',
    position: { x: rawPositions[i].x - cx, y: rawPositions[i].y - cy },
    width:    nodeWidth,
    height:   nodeHeight,
    data:     node as NodeData,
  }));

  const rfEdges: TopologyEdge[] = edges.map((edge) => ({
    id:     edge.id,
    source: edge.source,
    target: edge.target,
    type:   'routerEdge',
    data:   edge as EdgeData,
  }));

  return { nodes: rfNodes, edges: rfEdges };
}
