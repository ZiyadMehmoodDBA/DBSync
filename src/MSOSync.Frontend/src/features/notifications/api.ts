import client from '../../shared/api/client';
import type { NotificationPageDto } from './types';

const BASE = '/notifications';

export async function getNotifications(
  cursor: string | null,
  pageSize: number,
  unreadOnly: boolean,
  options?: { signal?: AbortSignal; severity?: string },
): Promise<NotificationPageDto> {
  const params: Record<string, unknown> = { pageSize, unreadOnly };
  if (cursor) params.cursor = cursor;
  if (options?.severity) params.severity = options.severity;
  const { data } = await client.get<NotificationPageDto>(BASE, { params, signal: options?.signal });
  return data;
}

export async function getUnreadCount(options?: { signal?: AbortSignal }): Promise<number> {
  const { data } = await client.get<{ count: number }>(`${BASE}/unread-count`, {
    signal: options?.signal,
  });
  return data.count;
}

export async function markRead(id: number): Promise<void> {
  await client.post(`${BASE}/${id}/read`);
}

export async function markAllRead(): Promise<void> {
  await client.post(`${BASE}/read-all`);
}
