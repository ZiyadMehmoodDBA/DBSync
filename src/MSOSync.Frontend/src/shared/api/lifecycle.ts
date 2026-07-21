import client from './client';
import type {
  LifecycleHistoryFilter, LifecycleHistoryPage, NodeStateDto, TransitionsDto,
} from '../types/lifecycle';

const base = (nodeId: string) => `/node-lifecycle/nodes/${encodeURIComponent(nodeId)}`;

export async function getNodeState(nodeId: string, options?: { signal?: AbortSignal }): Promise<NodeStateDto> {
  const { data } = await client.get<NodeStateDto>(`${base(nodeId)}/state`, options);
  return data;
}

export async function getNodeTransitions(nodeId: string, options?: { signal?: AbortSignal }): Promise<TransitionsDto> {
  const { data } = await client.get<TransitionsDto>(`${base(nodeId)}/transitions`, options);
  return data;
}

export async function getNodeLifecycleHistory(
  nodeId: string, filter: LifecycleHistoryFilter = {}, options?: { signal?: AbortSignal },
): Promise<LifecycleHistoryPage> {
  const { data } = await client.get<LifecycleHistoryPage>(`${base(nodeId)}/history`, {
    params: filter, ...options,
  });
  return data;
}

export async function enableNode(nodeId: string): Promise<void> {
  await client.post(`${base(nodeId)}/enable`);
}

export async function disableNode(nodeId: string, reason?: string): Promise<void> {
  await client.post(`${base(nodeId)}/disable`, { reason: reason ?? null });
}

export async function startMaintenance(
  nodeId: string, body: { reason: string; expectedEndAt?: string; notifyNode: boolean },
): Promise<void> {
  await client.post(`${base(nodeId)}/maintenance/start`, body);
}

export async function endMaintenance(nodeId: string): Promise<void> {
  await client.post(`${base(nodeId)}/maintenance/end`);
}

export async function decommissionNode(
  nodeId: string, body: { reason: string; gracePeriodMinutes?: number },
): Promise<void> {
  await client.post(`${base(nodeId)}/decommission`, body);
}

export async function forceCompleteDecommission(nodeId: string): Promise<void> {
  await client.post(`${base(nodeId)}/decommission/force`);
}

export async function drainNode(nodeId: string, reason?: string): Promise<void> {
  await client.post(`${base(nodeId)}/drain`, { reason: reason ?? null });
}

export async function resumeDrain(nodeId: string, reason?: string): Promise<void> {
  await client.post(`${base(nodeId)}/resume-drain`, { reason: reason ?? null });
}
