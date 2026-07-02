export const PreferenceKeys = {
  // Events page
  eventsFilter:          'page.events.filter',
  eventsPageSize:        'page.events.pageSize',
  eventsSort:            'page.events.sort',
  eventsColumns:         'page.events.columns',
  // Incoming batches page
  incomingFilter:        'page.incoming-batches.filter',
  incomingPageSize:      'page.incoming-batches.pageSize',
  incomingSort:          'page.incoming-batches.sort',
  incomingColumns:       'page.incoming-batches.columns',
  // Outgoing batches page
  outgoingFilter:        'page.outgoing-batches.filter',
  outgoingPageSize:      'page.outgoing-batches.pageSize',
  outgoingSort:          'page.outgoing-batches.sort',
  outgoingColumns:       'page.outgoing-batches.columns',
  // Audit page
  auditFilter:           'page.audit.filter',
  auditPageSize:         'page.audit.pageSize',
  auditSort:             'page.audit.sort',
  auditColumns:          'page.audit.columns',
  // Nodes page
  nodesColumns:          'page.nodes.columns',
  nodesPageSize:         'page.nodes.pageSize',
  // Users page
  usersColumns:          'page.users.columns',
  usersPageSize:         'page.users.pageSize',
  // Parameters page
  parametersColumns:     'page.parameters.columns',
  // UI preferences
  theme:                 'ui.theme',
  defaultLandingPage:    'ui.defaultLandingPage',
  autoRefreshEnabled:    'ui.autoRefresh.enabled',
  autoRefreshInterval:   'ui.autoRefresh.intervalSeconds',
  notificationsEnabled:  'ui.notifications.enabled',
} as const;

export type PreferenceKey   = typeof PreferenceKeys[keyof typeof PreferenceKeys];
export type Theme           = 'light' | 'dark';
export type SortPreference  = { field: string; direction: 'asc' | 'desc' };
