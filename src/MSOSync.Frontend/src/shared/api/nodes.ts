import client from './client';
import type { NodeDto } from '../types';

export async function getNodes(): Promise<NodeDto[]> {
  const { data } = await client.get<NodeDto[]>('/nodes');
  return data;
}

export async function enableNode(nodeId: string): Promise<void> {
  await client.post(`/nodes/${encodeURIComponent(nodeId)}/enable`);
}

export async function disableNode(nodeId: string): Promise<void> {
  await client.post(`/nodes/${encodeURIComponent(nodeId)}/disable`);
}

export async function approveRegistration(requestId: string): Promise<void> {
  await client.post(`/nodes/registrations/${encodeURIComponent(requestId)}/approve`);
}

export interface UpdateNodeRequest {
  groupId: string;
  syncUrl: string;
  heartbeatInterval: number;
}

export async function updateNode(nodeId: string, data: UpdateNodeRequest): Promise<void> {
  await client.put(`/nodes/${encodeURIComponent(nodeId)}`, data);
}

export interface CreateNodeRequest {
  nodeId: string;
  groupId: string;
  syncUrl: string;
  heartbeatInterval: number;
  transportMode: 'Pull' | 'Push';
  upstreamNodeId?: string;
  dbServer?: string;
  dbName?: string;
  dbAuthMode?: string;
  dbUser?: string;
  dbPassword?: string;
}

export interface CreateNodeResult {
  nodeId: string;
  nodeToken: string;
  node: NodeDto;
}

export async function createNode(data: CreateNodeRequest): Promise<CreateNodeResult> {
  const { data: result } = await client.post<CreateNodeResult>('/nodes', data);
  return result;
}
