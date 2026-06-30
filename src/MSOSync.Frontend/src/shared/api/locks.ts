import client from './client';
import type { LockDto } from '../types';

export async function getLocks(): Promise<LockDto[]> {
  const { data } = await client.get<LockDto[]>('/locks');
  return data;
}

export async function releaseLock(lockName: string): Promise<void> {
  await client.delete(`/locks/${encodeURIComponent(lockName)}`);
}
