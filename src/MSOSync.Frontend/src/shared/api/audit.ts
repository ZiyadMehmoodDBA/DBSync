import client from './client';
import type { AuditDto, AuditFilter } from '../types';
import type { CursorPageResult } from '../types/common';
import type { AuditSummaryDto } from '../types/audit-summary';

export type CursorAuditFilter = Omit<AuditFilter, 'page'> & {
  cursor?: string;
  includeTotalCount?: boolean;
};

export async function getAuditLog(
  filter: CursorAuditFilter,
  options?: { signal?: AbortSignal },
): Promise<CursorPageResult<AuditDto>> {
  const { data } = await client.get<CursorPageResult<AuditDto>>('/audit', {
    params: filter,
    signal: options?.signal,
  });
  return data;
}

export async function getAuditSummary(from: string, to: string): Promise<AuditSummaryDto> {
  const { data } = await client.get<AuditSummaryDto>('/audit/summary', {
    params: { from, to },
  });
  return data;
}
