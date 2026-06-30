import client from './client';
import type { TopologySummaryDto, TopologyGroupDto, TopologyGroupNodeDto } from '../types';

export async function getTopologySummary(): Promise<TopologySummaryDto> {
  const { data } = await client.get<TopologySummaryDto>('/topology/summary');
  return data;
}

export async function getTopologyGroups(): Promise<TopologyGroupDto[]> {
  const { data } = await client.get<TopologyGroupDto[]>('/topology/groups');
  return data;
}

export async function getTopologyGroupNodes(groupId: string): Promise<TopologyGroupNodeDto[]> {
  const { data } = await client.get<TopologyGroupNodeDto[]>(`/topology/groups/${groupId}/nodes`);
  return data;
}

export interface TopologyGraphNodeDto {
  id: string;
  groupId: string;
  label: string;
  status: number;
  memberCount: number;
  triggerCount: number;
  channelCount: number;
}

export interface TopologyGraphEdgeDto {
  id: string;
  source: string;
  target: string;
  channelIds: string[];
  isEnabled: boolean;
}

export interface TopologyGraphMetaDto {
  totalGroups: number;
  totalNodes: number;
  onlineNodes: number;
  generatedAt: string;
}

export interface TopologyGraphDto {
  nodes: TopologyGraphNodeDto[];
  edges: TopologyGraphEdgeDto[];
  meta: TopologyGraphMetaDto;
}

export async function getTopologyGraph(): Promise<TopologyGraphDto> {
  const { data } = await client.get<TopologyGraphDto>('/topology/graph');
  return data;
}
