export const PermissionKeys = {
  ViewEvents:          'VIEW_EVENTS',
  ViewMetrics:         'VIEW_METRICS',
  ViewAudit:           'VIEW_AUDIT',
  ViewTopology:        'VIEW_TOPOLOGY',
  ExportData:          'EXPORT_DATA',
  RetryBatches:        'RETRY_BATCHES',
  ApproveNodes:        'APPROVE_NODES',
  ReleaseLocks:        'RELEASE_LOCKS',
  EditParameters:      'EDIT_PARAMETERS',
  ManageTriggers:      'MANAGE_TRIGGERS',
  ManageRouters:       'MANAGE_ROUTERS',
  ManageUsers:         'MANAGE_USERS',
  ProvisionNodes:      'PROVISION_NODES',
  ManageNodeLifecycle:   'MANAGE_NODE_LIFECYCLE',
  ManageConfigurations:  'MANAGE_CONFIGURATIONS',
  ManagePlugins:         'MANAGE_PLUGINS',
} as const;

export type PermissionKey = (typeof PermissionKeys)[keyof typeof PermissionKeys];

export interface EffectivePermissionsDto {
  role: string;
  permissions: PermissionKey[];
  updatedAt: string;
}

export interface PermissionDto {
  permissionKey: PermissionKey;
  description: string;
  category: string;
}

export interface RolePermissionsDto {
  roleName: string;
  permissions: PermissionDto[];
}
