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
