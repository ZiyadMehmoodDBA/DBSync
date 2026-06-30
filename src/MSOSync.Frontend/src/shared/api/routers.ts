import client from './client';
import type { RouterDto } from '../types';

export async function getRouters(): Promise<RouterDto[]> {
  const { data } = await client.get<RouterDto[]>('/routers');
  return data;
}

export interface CreateRouterRequest {
  routerId: string;
  sourceNodeGroup: string;  // backend request field name
  targetNodeGroup: string;  // backend request field name
  routerType: string;
}

export interface UpdateRouterRequest {
  sourceNodeGroup: string;
  targetNodeGroup: string;
  routerType: string;
}

export async function createRouter(data: CreateRouterRequest): Promise<void> {
  await client.post('/routers', data);
}

export async function updateRouter(routerId: string, data: UpdateRouterRequest): Promise<void> {
  await client.put(`/routers/${encodeURIComponent(routerId)}`, data);
}

export async function deleteRouter(routerId: string): Promise<void> {
  await client.delete(`/routers/${encodeURIComponent(routerId)}`);
}
