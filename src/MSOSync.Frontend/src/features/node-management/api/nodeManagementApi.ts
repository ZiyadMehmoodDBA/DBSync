import client from '../../../shared/api/client';
import type {
  RegistrationSummaryDto,
  RegistrationDetailDto,
  RegistrationListFilter,
  CursorPageResult,
} from '../types/registration';
import type {
  NodeManagementOverviewDto,
  ProvisionRequest,
  ProvisionResult,
  ProvisionPackageRequest,
} from '../types/provision';

const BASE = '/node-management';

export async function getOverview(): Promise<NodeManagementOverviewDto> {
  const { data } = await client.get<NodeManagementOverviewDto>(`${BASE}/overview`);
  return data;
}

export async function getRegistrations(
  filter: RegistrationListFilter,
  signal?: AbortSignal,
): Promise<CursorPageResult<RegistrationSummaryDto>> {
  const { data } = await client.get<CursorPageResult<RegistrationSummaryDto>>(
    `${BASE}/registrations`,
    { params: filter, signal },
  );
  return data;
}

export async function getRegistrationDetail(
  id: number,
  signal?: AbortSignal,
): Promise<RegistrationDetailDto> {
  const { data } = await client.get<RegistrationDetailDto>(
    `${BASE}/registrations/${id}`,
    { signal },
  );
  return data;
}

export interface ApproveResultDto {
  registrationId: number;
  bootstrapToken: string | null;
}

export async function approveRegistration(
  id: number,
  notes?: string,
): Promise<ApproveResultDto> {
  const { data } = await client.post<ApproveResultDto>(`${BASE}/registrations/${id}/approve`, { notes });
  return data;
}

export async function rejectRegistration(
  id: number,
  reason?: string,
): Promise<void> {
  await client.post(`${BASE}/registrations/${id}/reject`, { reason });
}

export interface BulkResultItem {
  id: number;
  status: string;
}

export async function bulkApproveRegistrations(
  ids: number[],
): Promise<BulkResultItem[]> {
  const { data } = await client.post<BulkResultItem[]>(
    `${BASE}/registrations/bulk-approve`,
    { ids },
  );
  return data;
}

export async function bulkRejectRegistrations(
  ids: number[],
  reason?: string,
): Promise<BulkResultItem[]> {
  const { data } = await client.post<BulkResultItem[]>(
    `${BASE}/registrations/bulk-reject`,
    { ids, reason },
  );
  return data;
}

export async function provision(request: ProvisionRequest): Promise<ProvisionResult> {
  const { data } = await client.post<ProvisionResult>(`${BASE}/provision`, request);
  return data;
}

export async function downloadProvisionPackage(
  request: ProvisionPackageRequest,
): Promise<Blob> {
  const { data } = await client.post<Blob>(`${BASE}/provision-package`, request, {
    responseType: 'blob',
  });
  return data;
}
