import client from './client';
import type { EventSummaryDto, EventFilter } from '../types';
import type { PagedResult } from '../types/common';

export async function getEvents(filter: EventFilter): Promise<PagedResult<EventSummaryDto>> {
  const { data } = await client.get<PagedResult<EventSummaryDto>>('/events', {
    params: filter,
  });
  return data;
}
