import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import {
  getTemplates, getTemplate, getTemplateVersions, getTemplateVersion,
  getNodeConfiguration, getNodeConfigurationHistory,
  getDriftSummary, getDriftNodes, getRolloutStatus,
} from '../../shared/api/configuration';

export function useTemplates(statusFilter?: string) {
  return useQuery({
    queryKey: queryKeys.configurationTemplates(statusFilter),
    queryFn: ({ signal }) => getTemplates(statusFilter, { signal }),
    staleTime: 30_000,
  });
}

export function useTemplate(id: string) {
  return useQuery({
    queryKey: queryKeys.configurationTemplate(id),
    queryFn: ({ signal }) => getTemplate(id, { signal }),
    staleTime: 30_000,
    enabled: !!id,
  });
}

export function useTemplateVersions(id: string) {
  return useQuery({
    queryKey: queryKeys.configurationTemplateVersions(id),
    queryFn: ({ signal }) => getTemplateVersions(id, { signal }),
    staleTime: 60_000,
    enabled: !!id,
  });
}

export function useTemplateVersion(id: string, version: number) {
  return useQuery({
    queryKey: queryKeys.configurationTemplateVersion(id, version),
    queryFn: ({ signal }) => getTemplateVersion(id, version, { signal }),
    staleTime: 300_000,
    enabled: !!id && version > 0,
  });
}

export function useNodeConfiguration(nodeId: string) {
  return useQuery({
    queryKey: queryKeys.nodeConfiguration(nodeId),
    queryFn: ({ signal }) => getNodeConfiguration(nodeId, { signal }),
    staleTime: 15_000,
    enabled: !!nodeId,
  });
}

export function useNodeConfigurationHistory(nodeId: string) {
  return useQuery({
    queryKey: queryKeys.nodeConfigurationHistory(nodeId),
    queryFn: ({ signal }) => getNodeConfigurationHistory(nodeId, { signal }),
    staleTime: 30_000,
    enabled: !!nodeId,
  });
}

export function useConfigurationSummary() {
  return useQuery({
    queryKey: queryKeys.configurationSummary(),
    queryFn: ({ signal }) => getDriftSummary({ signal }),
    staleTime: 30_000,
    refetchInterval: 60_000,
  });
}

export function useDriftSummary(filter?: Record<string, unknown>) {
  return useQuery({
    queryKey: queryKeys.driftSummary(filter),
    queryFn: ({ signal }) => getDriftSummary({ signal }),
    staleTime: 15_000,
  });
}

export function useDriftNodes(filter?: {
  state?: string;
  templateId?: string;
  version?: number;
  nodeGroup?: string;
  search?: string;
}) {
  return useQuery({
    queryKey: queryKeys.driftNodes(filter as Record<string, unknown>),
    queryFn: ({ signal }) => getDriftNodes(filter, { signal }),
    staleTime: 15_000,
  });
}

export function useRolloutStatus(rolloutId: string | null) {
  return useQuery({
    queryKey: queryKeys.rolloutStatus(rolloutId!),
    queryFn: ({ signal }) => getRolloutStatus(rolloutId!, { signal }),
    enabled: !!rolloutId,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === 'Completed' || status === 'Failed' || status === 'Cancelled'
        ? false
        : 2_000;
    },
  });
}
