import { z } from 'zod';
import type { NodeDto } from '../../shared/types';

export const updateNodeSchema = z.object({
  groupId: z.string().min(1, 'Group is required'),
  syncUrl: z.string().url('Must be a valid URL including http:// or https://'),
  heartbeatInterval: z.coerce.number().int().min(1).max(1440),
});
export type UpdateNodeForm = z.infer<typeof updateNodeSchema>;

export function getDefaultValues(initialValues: NodeDto): UpdateNodeForm {
  return {
    groupId: initialValues.groupId,
    syncUrl: initialValues.syncUrl,
    heartbeatInterval: initialValues.heartbeatInterval,
  };
}
