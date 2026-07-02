import client from './client';
import type { EffectivePermissionsDto, PermissionDto, RolePermissionsDto, PermissionKey } from '../types/permissions';

export async function getMyPermissions(): Promise<EffectivePermissionsDto> {
  return client.get<EffectivePermissionsDto>('/me/permissions').then(r => r.data);
}

export async function getPermissionCatalog(): Promise<PermissionDto[]> {
  return client.get<PermissionDto[]>('/permissions').then(r => r.data);
}

export async function getRoles(): Promise<RolePermissionsDto[]> {
  return client.get<RolePermissionsDto[]>('/roles').then(r => r.data);
}

export async function getRoleDetail(roleName: string): Promise<RolePermissionsDto> {
  return client.get<RolePermissionsDto>(`/roles/${roleName}`).then(r => r.data);
}

export async function grantPermission(roleName: string, key: PermissionKey): Promise<void> {
  await client.put(`/roles/${roleName}/permissions/${key}`);
}

export async function revokePermission(roleName: string, key: PermissionKey): Promise<void> {
  await client.delete(`/roles/${roleName}/permissions/${key}`);
}

export async function resetRole(roleName: string): Promise<void> {
  await client.post(`/roles/${roleName}/reset`);
}

export async function copyFrom(targetRole: string, sourceRole: string): Promise<void> {
  await client.post(`/roles/${targetRole}/copy-from/${sourceRole}`);
}
