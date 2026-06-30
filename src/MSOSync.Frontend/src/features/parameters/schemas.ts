import { z } from 'zod';
import type { ParameterRow } from './columns';

export const updateParameterSchema = z.object({
  value: z.string().trim().min(1, 'Value is required'),
});
export type UpdateParameterForm = z.infer<typeof updateParameterSchema>;

export function getDefaultValues(_initialValues?: ParameterRow): UpdateParameterForm {
  // Always start empty — never prefill secret values
  return { value: '' };
}
