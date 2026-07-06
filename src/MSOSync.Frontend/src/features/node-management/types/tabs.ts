export const NODE_MANAGEMENT_TABS = {
  OVERVIEW:      'overview',
  REGISTRATIONS: 'registrations',
  PROVISION:     'provision',
  NODES:         'nodes',
  GROUPS:        'groups',
} as const;

export type TabId = (typeof NODE_MANAGEMENT_TABS)[keyof typeof NODE_MANAGEMENT_TABS];
