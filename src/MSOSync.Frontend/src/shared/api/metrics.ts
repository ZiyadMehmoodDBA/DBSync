import client from './client';
import type {
  MetricsSummaryDto,
  NodeMetricsDto,
  ChannelMetricsDto,
  RuntimeMetricsDto,
} from '../types';

export async function getMetricsSummary(): Promise<MetricsSummaryDto> {
  const { data } = await client.get<MetricsSummaryDto>('/metrics/summary');
  return data;
}

export async function getNodeMetrics(): Promise<NodeMetricsDto[]> {
  const { data } = await client.get<NodeMetricsDto[]>('/metrics/nodes');
  return data;
}

export async function getChannelMetrics(): Promise<ChannelMetricsDto[]> {
  const { data } = await client.get<ChannelMetricsDto[]>('/metrics/channels');
  return data;
}

export async function getRuntimeMetrics(): Promise<RuntimeMetricsDto> {
  const { data } = await client.get<RuntimeMetricsDto>('/metrics/runtime');
  return data;
}
