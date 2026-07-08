import client from './client';
import type {
  TemplateSummaryDto, TemplateDto, TemplateVersionDto, TemplateVersionSummaryDto,
  NodeConfigurationDto, ConfigurationHistoryEventDto,
  DriftSummaryDto, DriftNodeDto, RolloutDto,
  CreateTemplateRequest, UpdateDraftRequest, AssignRequest,
  SetOverrideRequest, StartRolloutRequest,
} from '../../features/configuration/types';

const TEMPLATES = '/configuration/templates';
const BASE = '/configuration';

export async function getTemplates(
  statusFilter?: string,
  options?: { signal?: AbortSignal },
): Promise<TemplateSummaryDto[]> {
  const { data } = await client.get<TemplateSummaryDto[]>(TEMPLATES, {
    params: statusFilter ? { status: statusFilter } : undefined,
    ...options,
  });
  return data;
}

export async function getTemplate(
  id: string,
  options?: { signal?: AbortSignal },
): Promise<TemplateDto> {
  const { data } = await client.get<TemplateDto>(`${TEMPLATES}/${id}`, options);
  return data;
}

export async function createTemplate(req: CreateTemplateRequest): Promise<TemplateDto> {
  const { data } = await client.post<TemplateDto>(TEMPLATES, req);
  return data;
}

export async function updateDraft(
  id: string,
  req: UpdateDraftRequest,
  rowVersion?: string,
): Promise<TemplateVersionDto> {
  const { data } = await client.put<TemplateVersionDto>(`${TEMPLATES}/${id}/draft`, req, {
    headers: rowVersion ? { 'If-Match': `"${rowVersion}"` } : undefined,
  });
  return data;
}

export async function validateTemplate(id: string): Promise<{ hashPreview?: string; effectiveSettings?: unknown }> {
  const { data } = await client.post(`${TEMPLATES}/${id}/validate`);
  return data;
}

export async function publishTemplate(id: string): Promise<TemplateVersionDto> {
  const { data } = await client.post<TemplateVersionDto>(`${TEMPLATES}/${id}/publish`);
  return data;
}

export async function cloneTemplate(id: string, newName: string): Promise<TemplateDto> {
  const { data } = await client.post<TemplateDto>(`${TEMPLATES}/${id}/clone`, { newName });
  return data;
}

export async function archiveTemplate(id: string): Promise<void> {
  await client.post(`${TEMPLATES}/${id}/archive`);
}

export async function getTemplateVersions(
  id: string,
  options?: { signal?: AbortSignal },
): Promise<TemplateVersionSummaryDto[]> {
  const { data } = await client.get<TemplateVersionSummaryDto[]>(`${TEMPLATES}/${id}/versions`, options);
  return data;
}

export async function getTemplateVersion(
  id: string,
  version: number,
  options?: { signal?: AbortSignal },
): Promise<TemplateVersionDto> {
  const { data } = await client.get<TemplateVersionDto>(`${TEMPLATES}/${id}/versions/${version}`, options);
  return data;
}

export async function assignTemplate(
  nodeId: string,
  req: AssignRequest,
): Promise<NodeConfigurationDto> {
  const { data } = await client.post<NodeConfigurationDto>(
    `${BASE}/nodes/${encodeURIComponent(nodeId)}/assign`,
    req,
  );
  return data;
}

export async function unassignTemplate(nodeId: string): Promise<void> {
  await client.delete(`${BASE}/nodes/${encodeURIComponent(nodeId)}/assign`);
}

export async function getNodeConfiguration(
  nodeId: string,
  options?: { signal?: AbortSignal },
): Promise<NodeConfigurationDto> {
  const { data } = await client.get<NodeConfigurationDto>(
    `${BASE}/nodes/${encodeURIComponent(nodeId)}`,
    options,
  );
  return data;
}

export async function getNodeConfigurationHistory(
  nodeId: string,
  options?: { signal?: AbortSignal },
): Promise<ConfigurationHistoryEventDto[]> {
  const { data } = await client.get<ConfigurationHistoryEventDto[]>(
    `${BASE}/nodes/${encodeURIComponent(nodeId)}/history`,
    options,
  );
  return data;
}

export async function setOverride(nodeId: string, req: SetOverrideRequest): Promise<void> {
  await client.post(`${BASE}/nodes/${encodeURIComponent(nodeId)}/overrides`, req);
}

export async function removeOverride(nodeId: string, key: string): Promise<void> {
  await client.delete(`${BASE}/nodes/${encodeURIComponent(nodeId)}/overrides/${encodeURIComponent(key)}`);
}

export async function startRollout(req: StartRolloutRequest): Promise<{ rolloutId: string; status: string }> {
  const { data } = await client.post<{ rolloutId: string; status: string }>(`${BASE}/rollout`, req);
  return data;
}

export async function getRolloutStatus(
  rolloutId: string,
  options?: { signal?: AbortSignal },
): Promise<RolloutDto> {
  const { data } = await client.get<RolloutDto>(`${BASE}/rollout/${rolloutId}`, options);
  return data;
}

export async function getDriftNodes(
  params?: {
    state?: string;
    templateId?: string;
    version?: number;
    nodeGroup?: string;
    search?: string;
  },
  options?: { signal?: AbortSignal },
): Promise<DriftNodeDto[]> {
  const { data } = await client.get<DriftNodeDto[]>(`${BASE}/drift`, {
    params,
    ...options,
  });
  return data;
}

export async function getDriftSummary(
  options?: { signal?: AbortSignal },
): Promise<DriftSummaryDto> {
  const { data } = await client.get<DriftSummaryDto>(`${BASE}/summary`, options);
  return data;
}
