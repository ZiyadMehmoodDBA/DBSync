import client from './client';
import type { AuditPage, ChainVerifyResult } from '../types/security';

export async function getAuditLog(page: number): Promise<AuditPage> {
  const { data } = await client.get<AuditPage>(`/security/audit?page=${page}&pageSize=50`);
  return data;
}

export async function getChainVerify(): Promise<ChainVerifyResult> {
  const { data } = await client.get<ChainVerifyResult>('/security/audit/verify');
  return data;
}
