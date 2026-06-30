import { z } from 'zod';

// Form uses string values for number inputs (HTML inputs return strings).
// Validation coerces and validates range; submission extracts typed numbers.
export const channelFormSchema = z.object({
  channelId: z.string().trim().min(1, 'Channel ID is required'),
  priority: z
    .string()
    .refine((v) => !isNaN(Number(v)) && Number.isInteger(Number(v)), 'Must be a whole number')
    .refine((v) => Number(v) >= 0 && Number(v) <= 100, 'Must be between 0 and 100'),
  batchSize: z
    .string()
    .refine((v) => !isNaN(Number(v)) && Number.isInteger(Number(v)), 'Must be a whole number')
    .refine((v) => Number(v) >= 1 && Number(v) <= 1_000_000, 'Must be between 1 and 1,000,000'),
  maxBatchToSend: z
    .string()
    .refine((v) => !isNaN(Number(v)) && Number.isInteger(Number(v)), 'Must be a whole number')
    .refine((v) => Number(v) >= 1 && Number(v) <= 10_000, 'Must be between 1 and 10,000'),
  maxDataSize: z
    .string()
    .refine((v) => !isNaN(Number(v)) && Number.isInteger(Number(v)), 'Must be a whole number')
    .refine((v) => Number(v) >= 1, 'Must be at least 1'),
});
export type ChannelForm = z.infer<typeof channelFormSchema>;

// Create schema = full schema
export const createChannelSchema = channelFormSchema;
export type CreateChannelForm = ChannelForm;

// Update schema = same shape but channelId not validated (field hidden; value ignored)
export const updateChannelSchema = channelFormSchema;
export type UpdateChannelForm = ChannelForm;

export const CHANNEL_FORM_DEFAULTS: ChannelForm = {
  channelId: '',
  priority: '0',
  batchSize: '1000',
  maxBatchToSend: '10',
  maxDataSize: '1048576',
};
