import { z } from 'zod';

export const createChannelSchema = z.object({
  channelId: z.string().trim().min(1, 'Channel ID is required'),
  priority: z.coerce.number().int().min(0).max(100),
  batchSize: z.coerce.number().int().min(1).max(1_000_000),
  maxBatchToSend: z.coerce.number().int().min(1).max(10_000),
  maxDataSize: z.coerce.number().int().min(1),
});

export const updateChannelSchema = createChannelSchema.omit({ channelId: true });

export type CreateChannelForm = z.infer<typeof createChannelSchema>;
export type UpdateChannelForm = z.infer<typeof updateChannelSchema>;

export const CHANNEL_FORM_DEFAULTS: CreateChannelForm = {
  channelId: '',
  priority: 0,
  batchSize: 1000,
  maxBatchToSend: 10,
  maxDataSize: 1048576,
};
