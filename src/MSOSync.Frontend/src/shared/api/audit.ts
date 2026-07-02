import client from './client';
import type { AuditDto, AuditFilter } from '../types';
import type { PagedResult } from '../types/common';
import type { AuditSummaryDto } from '../types/audit-summary';

export async function getAuditLog(filter: AuditFilter): Promise<PagedResult<AuditDto>> {
  const { data } = await client.get<PagedResult<AuditDto>>('/audit', {
    params: filter,
  });
  return data;
}

export async function getAuditSummary(from: string, to: string): Promise<AuditSummaryDto> {
  const { data } = await client.get<AuditSummaryDto>('/audit/summary', {
    params: { from, to },
  });
  return data;
}
