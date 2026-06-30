import client from './client';
import type { UserSummaryDto, UserFilter } from '../types';
import type { PagedResult } from '../types/common';

export async function getUsers(filter: UserFilter): Promise<PagedResult<UserSummaryDto>> {
  const { data } = await client.get<PagedResult<UserSummaryDto>>('/users', {
    params: filter,
  });
  return data;
}
