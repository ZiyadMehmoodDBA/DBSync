import client from './client';
import type {
  OverviewDto,
  SystemInfoDto,
  WorkerStatusDto,
  HealthContributionDto,
} from '../types/system';

export async function fetchOverview(): Promise<OverviewDto> {
  const { data } = await client.get<OverviewDto>('/system/overview');
  return data;
}

export async function fetchSystemInfo(): Promise<SystemInfoDto> {
  const { data } = await client.get<SystemInfoDto>('/system/info');
  return data;
}

export async function fetchWorkers(): Promise<WorkerStatusDto[]> {
  const { data } = await client.get<WorkerStatusDto[]>('/system/workers');
  return data;
}

export async function fetchSystemHealth(): Promise<HealthContributionDto[]> {
  const { data } = await client.get<HealthContributionDto[]>('/system/health');
  return data;
}

export const systemKeys = {
  overview: ['system', 'overview'] as const,
  info:     ['system', 'info'] as const,
  workers:  ['system', 'workers'] as const,
  health:   ['system', 'health'] as const,
};
