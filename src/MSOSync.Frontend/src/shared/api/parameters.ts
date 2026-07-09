import client from './client';
import type { ParameterDto, ParameterDescriptorDto, ParameterMetadataDto } from '../types';

export async function getParameters(): Promise<ParameterDto[]> {
  const { data } = await client.get<ParameterDto[]>('/parameters');
  return data;
}

export async function getParameterDescriptors(): Promise<ParameterDescriptorDto[]> {
  const { data } = await client.get<ParameterDescriptorDto[]>('/parameters/descriptors');
  return data;
}

export async function updateParameter(name: string, value: string): Promise<void> {
  await client.put(`/parameters/${encodeURIComponent(name)}`, { value });
}

/** Fetch enriched parameters, optionally filtered by category */
export async function getParametersByCategory(category?: string): Promise<ParameterMetadataDto[]> {
  const url = category ? `/parameters?category=${encodeURIComponent(category)}` : '/parameters';
  const { data } = await client.get<ParameterMetadataDto[]>(url);
  return data;
}

/** Update a named parameter value */
export async function updateParameterByName(name: string, value: string): Promise<void> {
  await client.put(`/parameters/${encodeURIComponent(name)}`, { value });
}
