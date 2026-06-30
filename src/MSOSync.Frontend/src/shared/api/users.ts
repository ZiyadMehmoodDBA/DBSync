import client from './client';
import type { UserSummaryDto, UserFilter } from '../types';
import type { PagedResult } from '../types/common';

export async function getUsers(filter: UserFilter): Promise<PagedResult<UserSummaryDto>> {
  const { data } = await client.get<PagedResult<UserSummaryDto>>('/users', {
    params: filter,
  });
  return data;
}

export interface CreateUserRequest {
  username: string;
  password: string;
  enabled: boolean;
}

export interface UpdateUserRequest {
  enabled: boolean;
  newPassword?: string;
}

export async function createUser(data: CreateUserRequest): Promise<void> {
  await client.post('/users', data);
}

export async function updateUser(userId: number, data: UpdateUserRequest): Promise<void> {
  await client.put(`/users/${userId}`, data);
}

export async function deactivateUser(userId: number): Promise<void> {
  await client.delete(`/users/${userId}`);
}
