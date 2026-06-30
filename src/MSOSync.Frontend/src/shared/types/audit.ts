export interface AuditDto {
  auditId: number;
  username: string;
  actionName: string;
  objectName?: string;
  correlationId?: string;
  createTime: string;
}

export interface AuditFilter {
  username?: string;
  actionName?: string;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}
