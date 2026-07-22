import client from './client';
import type { ConfigVersionDiffDto } from '../types/configComparison';

export const configCompareKeys = {
  diff: (templateId: string, v1: number, v2: number) =>
    ['config-compare', templateId, v1, v2] as const,
} as const;

export async function getConfigVersionDiff(
  templateId: string,
  v1: number,
  v2: number,
  options?: { signal?: AbortSignal },
): Promise<ConfigVersionDiffDto> {
  const { data } = await client.get<ConfigVersionDiffDto>(
    `/configuration/templates/${encodeURIComponent(templateId)}/compare`,
    { params: { v1, v2 }, ...options },
  );
  return data;
}
