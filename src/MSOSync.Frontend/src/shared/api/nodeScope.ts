import client from './client';
import type { NodeScopeDto, SetNodeScopeRequest } from '../types';

export async function getNodeScope(nodeId: string): Promise<NodeScopeDto> {
  const { data } = await client.get<NodeScopeDto>(`/nodes/${encodeURIComponent(nodeId)}/scope`);
  return data;
}

export async function setNodeScope(nodeId: string, req: SetNodeScopeRequest): Promise<NodeScopeDto> {
  const { data } = await client.put<NodeScopeDto>(`/nodes/${encodeURIComponent(nodeId)}/scope`, req);
  return data;
}
