import type { RegistrationListFilter } from '../types/registration';

export const nodeManagementKeys = {
  overview:           (): readonly unknown[] => ['node-management', 'overview'],
  registrations:      (f: RegistrationListFilter): readonly unknown[] =>
                        ['node-management', 'registrations', f],
  registrationDetail: (id: number): readonly unknown[] =>
                        ['node-management', 'registrations', id],
  nodes:              (): readonly unknown[] => ['node-management', 'nodes'],
  groups:             (): readonly unknown[] => ['node-management', 'groups'],
} as const;
