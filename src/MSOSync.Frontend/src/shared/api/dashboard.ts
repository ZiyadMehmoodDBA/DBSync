import client from './client';
import type { DashboardSummaryDto, ActivityItemDto } from '../types';
import type { PagedResult } from '../types/common';

export async function getDashboardSummary(): Promise<DashboardSummaryDto> {
  const { data } = await client.get<DashboardSummaryDto>('/dashboard/summary');
  return data;
}

export async function getDashboardActivity(
  page = 1,
  pageSize = 20,
): Promise<PagedResult<ActivityItemDto>> {
  const { data } = await client.get<PagedResult<ActivityItemDto>>('/dashboard/activity', {
    params: { page, pageSize },
  });
  return data;
}
