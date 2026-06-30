import client from './client';
import type { RouterDto } from '../types';

export async function getRouters(): Promise<RouterDto[]> {
  const { data } = await client.get<RouterDto[]>('/routers');
  return data;
}
