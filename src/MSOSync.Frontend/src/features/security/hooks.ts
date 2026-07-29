import { useQuery } from '@tanstack/react-query';
import { getAuditLog, getChainVerify } from '../../shared/api/security';

const securityKeys = {
  auditLog: (page: number) => ['security', 'audit', page] as const,
  chainVerify: () => ['security', 'chain-verify'] as const,
};

export function useAuditLog(page: number) {
  return useQuery({
    queryKey: securityKeys.auditLog(page),
    queryFn: () => getAuditLog(page),
    staleTime: 30_000,
  });
}

export function useChainVerify() {
  return useQuery({
    queryKey: securityKeys.chainVerify(),
    queryFn: getChainVerify,
    staleTime: 60_000,
  });
}
