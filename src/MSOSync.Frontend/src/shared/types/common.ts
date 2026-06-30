export interface PagedResult<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ApiError {
  title: string;
  status: number;
  detail?: string;
  traceId?: string;
}
