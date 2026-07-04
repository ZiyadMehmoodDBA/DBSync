import client from './client';
import type { ExportJobDto, CreateExportJobRequest } from '../types/export';

export async function createExportJob(request: CreateExportJobRequest): Promise<{ jobId: string }> {
  const { data } = await client.post<{ jobId: string }>('/export-jobs', request);
  return data;
}

export async function getExportJobs(): Promise<ExportJobDto[]> {
  const { data } = await client.get<ExportJobDto[]>('/export-jobs');
  return data;
}

export async function deleteExportJob(jobId: string): Promise<void> {
  await client.delete(`/export-jobs/${jobId}`);
}

export function getDownloadUrl(jobId: string): string {
  return `/api/v1/export-jobs/${jobId}/download`;
}
