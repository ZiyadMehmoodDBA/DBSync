import client from './client';
import type { ChannelDto } from '../types';

export async function getChannels(): Promise<ChannelDto[]> {
  const { data } = await client.get<ChannelDto[]>('/channels');
  return data;
}
