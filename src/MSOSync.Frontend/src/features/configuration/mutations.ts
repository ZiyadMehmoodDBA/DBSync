import { useMutation, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import {
  createTemplate, updateDraft, publishTemplate, cloneTemplate, archiveTemplate,
  assignTemplate, unassignTemplate, setOverride, removeOverride, startRollout,
} from '../../shared/api/configuration';
import type {
  CreateTemplateRequest, UpdateDraftRequest, AssignRequest,
  SetOverrideRequest, StartRolloutRequest,
} from './types';

export function useCreateTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateTemplateRequest) => createTemplate(data),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.configurationTemplates() }),
  });
}

export function useUpdateDraft(templateId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ data, rowVersion }: { data: UpdateDraftRequest; rowVersion?: string }) =>
      updateDraft(templateId, data, rowVersion),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.configurationTemplate(templateId) });
      qc.invalidateQueries({ queryKey: queryKeys.configurationTemplates() });
    },
  });
}

export function usePublishTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (templateId: string) => publishTemplate(templateId),
    onSuccess: (_data, templateId) => {
      qc.invalidateQueries({ queryKey: queryKeys.configurationTemplate(templateId) });
      qc.invalidateQueries({ queryKey: queryKeys.configurationTemplates() });
      qc.invalidateQueries({ queryKey: queryKeys.configurationTemplateVersions(templateId) });
    },
  });
}

export function useCloneTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, newName }: { id: string; newName: string }) => cloneTemplate(id, newName),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.configurationTemplates() }),
  });
}

export function useArchiveTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => archiveTemplate(id),
    onSuccess: (_data, id) => {
      qc.invalidateQueries({ queryKey: queryKeys.configurationTemplate(id) });
      qc.invalidateQueries({ queryKey: queryKeys.configurationTemplates() });
    },
  });
}

export function useAssignTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ nodeId, data }: { nodeId: string; data: AssignRequest }) =>
      assignTemplate(nodeId, data),
    onSuccess: (_data, { nodeId }) => {
      qc.invalidateQueries({ queryKey: queryKeys.nodeConfiguration(nodeId) });
      qc.invalidateQueries({ queryKey: queryKeys.driftSummary() });
      qc.invalidateQueries({ queryKey: queryKeys.driftNodes() });
    },
  });
}

export function useUnassignTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (nodeId: string) => unassignTemplate(nodeId),
    onSuccess: (_data, nodeId) => {
      qc.invalidateQueries({ queryKey: queryKeys.nodeConfiguration(nodeId) });
      qc.invalidateQueries({ queryKey: queryKeys.driftSummary() });
    },
  });
}

export function useSetOverride(nodeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: SetOverrideRequest) => setOverride(nodeId, data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.nodeConfiguration(nodeId) });
    },
  });
}

export function useRemoveOverride(nodeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (key: string) => removeOverride(nodeId, key),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.nodeConfiguration(nodeId) });
    },
  });
}

export function useStartRollout() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: StartRolloutRequest) => startRollout(data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.driftSummary() });
      qc.invalidateQueries({ queryKey: queryKeys.driftNodes() });
    },
  });
}
