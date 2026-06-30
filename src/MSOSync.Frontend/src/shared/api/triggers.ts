import client from './client';
import type { TriggerDto } from '../types';

export async function getTriggers(): Promise<TriggerDto[]> {
  const { data } = await client.get<TriggerDto[]>('/triggers');
  return data;
}

export async function enableTrigger(triggerId: string): Promise<void> {
  await client.post(`/triggers/${encodeURIComponent(triggerId)}/enable`);
}

export async function disableTrigger(triggerId: string): Promise<void> {
  await client.post(`/triggers/${encodeURIComponent(triggerId)}/disable`);
}

export async function rebuildTrigger(triggerId: string): Promise<void> {
  await client.post(`/triggers/${encodeURIComponent(triggerId)}/rebuild`);
}

export async function verifyTrigger(triggerId: string): Promise<void> {
  await client.post(`/triggers/${encodeURIComponent(triggerId)}/verify`);
}

export interface CreateTriggerRequest {
  triggerId: string;
  sourceTable: string;
  channelId: string;
  syncOnInsert: boolean;
  syncOnUpdate: boolean;
  syncOnDelete: boolean;
}

export interface UpdateTriggerRequest {
  sourceTable: string;
  channelId: string;
  syncOnInsert: boolean;
  syncOnUpdate: boolean;
  syncOnDelete: boolean;
}

export async function createTrigger(data: CreateTriggerRequest): Promise<void> {
  await client.post('/triggers', data);
}

export async function updateTrigger(triggerId: string, data: UpdateTriggerRequest): Promise<void> {
  await client.put(`/triggers/${encodeURIComponent(triggerId)}`, data);
}

export async function deleteTrigger(triggerId: string): Promise<void> {
  await client.delete(`/triggers/${encodeURIComponent(triggerId)}`);
}
