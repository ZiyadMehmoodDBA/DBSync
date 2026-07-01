import { z } from 'zod';
import type { TriggerDto } from '../../shared/types';

const triggerBase = z.object({
  triggerId: z.string().trim().min(1, 'Trigger ID is required'),
  schemaName: z.string().trim().min(1, 'Schema name is required'),
  tableName: z.string().trim().min(1, 'Table name is required'),
  channelId: z.string().min(1, 'Channel is required'),
  syncOnInsert: z.boolean(),
  syncOnUpdate: z.boolean(),
  syncOnDelete: z.boolean(),
});

const atLeastOneOp = (x: { syncOnInsert: boolean; syncOnUpdate: boolean; syncOnDelete: boolean }) =>
  x.syncOnInsert || x.syncOnUpdate || x.syncOnDelete;
const atLeastOneOpOpts = {
  message: 'At least one sync operation must be enabled.',
  path: ['syncOnInsert'] as const,
};

export const createTriggerSchema = triggerBase.refine(atLeastOneOp, atLeastOneOpOpts);
export type CreateTriggerForm = z.infer<typeof createTriggerSchema>;

export const updateTriggerSchema = triggerBase.omit({ triggerId: true }).refine(atLeastOneOp, atLeastOneOpOpts);
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
