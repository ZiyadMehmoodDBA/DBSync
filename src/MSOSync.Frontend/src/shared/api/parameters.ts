import client from './client';
import type { ParameterDto, ParameterDescriptorDto } from '../types';

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
