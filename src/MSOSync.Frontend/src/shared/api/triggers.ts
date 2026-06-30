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
