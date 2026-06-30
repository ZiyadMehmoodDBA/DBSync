import client from './client';
import type { LockDto } from '../types';

export async function getLocks(): Promise<LockDto[]> {
  const { data } = await client.get<LockDto[]>('/locks');
  return data;
}
