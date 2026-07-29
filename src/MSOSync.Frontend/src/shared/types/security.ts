export interface AuditEntry {
  auditId: number;
  username: string | null;
  actionName: string | null;
  objectName: string | null;
  correlationId: string | null;
  createTime: string | null;
  entryHash: string | null;
}

export interface AuditPage {
  total: number;
  page: number;
  page_size: number;
  items: AuditEntry[];
}

export interface ChainVerifyResult {
  is_valid: boolean;
  first_broken_id: number | null;
}
