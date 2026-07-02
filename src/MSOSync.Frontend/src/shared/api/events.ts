import client from './client';
import type { EventSummaryDto, EventFilter } from '../types';
import type { CursorPageResult } from '../types/common';

export type CursorEventFilter = Omit<EventFilter, 'page'> & {
  cursor?: string;
  includeTotalCount?: boolean;
};

export async function getEvents(
  filter: CursorEventFilter,
  options?: { signal?: AbortSignal },
): Promise<CursorPageResult<EventSummaryDto>> {
  const { data } = await client.get<CursorPageResult<EventSummaryDto>>('/events', {
    params: filter,
    signal: options?.signal,
  });
  return data;
}
