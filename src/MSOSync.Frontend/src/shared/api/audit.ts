import client from './client';
import type { AuditDto, AuditFilter } from '../types';
import type { PagedResult } from '../types/common';

export async function getAuditLog(filter: AuditFilter): Promise<PagedResult<AuditDto>> {
  const { data } = await client.get<PagedResult<AuditDto>>('/audit', {
    params: filter,
  });
  return data;
}
