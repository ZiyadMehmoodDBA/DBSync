export interface UserSummaryDto {
  userId: number;
  username: string;
  enabled: boolean;
  roles: string[];
  createdTime: string;
  lastLoginTime?: string;
}

export interface UserFilter {
  page: number;
  pageSize: number;
  enabled?: boolean;
  search?: string;
}
