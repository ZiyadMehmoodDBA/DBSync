import client from './client';
import type { ChannelDto } from '../types';

export async function getChannels(): Promise<ChannelDto[]> {
  const { data } = await client.get<ChannelDto[]>('/channels');
  return data;
}

export interface CreateChannelRequest {
  channelId: string;
  priority: number;
  batchSize: number;
  maxBatchToSend: number;
  maxDataSize: number;
}

export interface UpdateChannelRequest {
  priority: number;
  batchSize: number;
  maxBatchToSend: number;
  maxDataSize: number;
}

export async function createChannel(data: CreateChannelRequest): Promise<void> {
  await client.post('/channels', data);
}

export async function updateChannel(channelId: string, data: UpdateChannelRequest): Promise<void> {
  await client.put(`/channels/${encodeURIComponent(channelId)}`, data);
}

export async function deleteChannel(channelId: string): Promise<void> {
  await client.delete(`/channels/${encodeURIComponent(channelId)}`);
}
