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
