import type { RegistrationFilter } from '../types/registration';

export const nodeManagementKeys = {
  overview:           (): readonly unknown[] => ['node-management', 'overview'],
  registrations:      (f?: RegistrationFilter): readonly unknown[] =>
                        f !== undefined
                          ? ['node-management', 'registrations', f]
                          : ['node-management', 'registrations'],
  registrationDetail: (id: number): readonly unknown[] =>
                        ['node-management', 'registrations', id],
  nodes:              (): readonly unknown[] => ['node-management', 'nodes'],
  groups:             (): readonly unknown[] => ['node-management', 'groups'],
} as const;
