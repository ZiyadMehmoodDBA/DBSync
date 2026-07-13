import client from './client';
import type { AuditDto, AuditFilter } from '../types';
import type { CursorPageResult } from '../types/common';
import type { AuditSummaryDto } from '../types/audit-summary';
import type { CorrelationTimelineDto, CorrelationSearchResultDto } from '../types/correlation';

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

export async function fetchCorrelationTimeline(
  correlationId: string,
): Promise<CorrelationTimelineDto> {
  const { data } = await client.get<CorrelationTimelineDto>(
    `/audit/correlation/${encodeURIComponent(correlationId)}`,
  );
  return data;
}

export async function searchCorrelations(
  params: Record<string, string>,
): Promise<CorrelationSearchResultDto[]> {
  const { data } = await client.get<CorrelationSearchResultDto[]>('/audit/correlations/search', {
    params,
  });
  return data;
}

export async function exportCorrelation(
  correlationId: string,
  format: 'json' | 'markdown',
): Promise<Blob> {
  const { data } = await client.get<Blob>(
    `/audit/correlation/${encodeURIComponent(correlationId)}/export`,
    { params: { format }, responseType: 'blob' },
  );
  return data;
}

export const correlationKeys = {
  timeline: (id: string) => ['correlation', 'timeline', id] as const,
  search: (params: Record<string, string>) => ['correlation', 'search', params] as const,
};
