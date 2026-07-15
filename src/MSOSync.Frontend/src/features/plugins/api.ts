import client from '../../shared/api/client';
import type { PluginDto, PluginSummaryDto } from './types';

export async function getPlugins(): Promise<PluginDto[]> {
  const { data } = await client.get<PluginDto[]>('/plugins');
  return data;
}

export async function getPluginSummary(): Promise<PluginSummaryDto> {
  const { data } = await client.get<PluginSummaryDto>('/plugins/summary');
  return data;
}

export async function enablePlugin(pluginId: string): Promise<void> {
  await client.post(`/plugins/${pluginId}/enable`);
}

export async function disablePlugin(pluginId: string): Promise<void> {
  await client.post(`/plugins/${pluginId}/disable`);
}
