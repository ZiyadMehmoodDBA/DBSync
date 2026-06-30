import { z } from 'zod';
import type { UserSummaryDto } from '../../shared/types';

export const createUserSchema = z.object({
  username: z.string().trim().min(3, 'Username must be at least 3 characters').max(100),
  password: z.string().min(8, 'Password must be at least 8 characters'),
  enabled: z.boolean(),
});
export type CreateUserForm = z.infer<typeof createUserSchema>;

export const updateUserSchema = z.object({
  enabled: z.boolean(),
  newPassword: z.string().min(8, 'Password must be at least 8 characters').optional().or(z.literal('')),
});
export type UpdateUserForm = z.infer<typeof updateUserSchema>;

export function getDefaultValues(
  initialValues?: UserSummaryDto,
  mode?: 'create' | 'edit',
): CreateUserForm | UpdateUserForm {
  if (mode === 'edit' && initialValues) {
    return { enabled: initialValues.enabled, newPassword: '' };
  }
  return { username: '', password: '', enabled: true };
}
