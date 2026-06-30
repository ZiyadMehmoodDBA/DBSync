import { z } from 'zod';
import type { TriggerDto } from '../../shared/types';

export const createTriggerSchema = z
  .object({
    triggerId: z.string().trim().min(1, 'Trigger ID is required'),
    schemaName: z.string().trim().min(1, 'Schema name is required'),
    tableName: z.string().trim().min(1, 'Table name is required'),
    channelId: z.string().min(1, 'Channel is required'),
    syncOnInsert: z.boolean(),
    syncOnUpdate: z.boolean(),
    syncOnDelete: z.boolean(),
  })
  .refine((x) => x.syncOnInsert || x.syncOnUpdate || x.syncOnDelete, {
    message: 'At least one sync operation must be enabled.',
    path: ['syncOnInsert'],
  });

export type CreateTriggerForm = z.infer<typeof createTriggerSchema>;

export const updateTriggerSchema = createTriggerSchema.omit({ triggerId: true });
export type UpdateTriggerForm = z.infer<typeof updateTriggerSchema>;

export function getDefaultValues(
  initialValues?: TriggerDto,
  mode?: 'create' | 'edit',
): CreateTriggerForm | UpdateTriggerForm {
  if (mode === 'edit' && initialValues) {
    return {
      schemaName: initialValues.schemaName,
      tableName: initialValues.tableName,
      channelId: initialValues.channelId,
      syncOnInsert: initialValues.captureInsert,
      syncOnUpdate: initialValues.captureUpdate,
      syncOnDelete: initialValues.captureDelete,
    };
  }
  return {
    triggerId: '',
    schemaName: 'dbo',
    tableName: '',
    channelId: '',
    syncOnInsert: true,
    syncOnUpdate: true,
    syncOnDelete: true,
  };
}
