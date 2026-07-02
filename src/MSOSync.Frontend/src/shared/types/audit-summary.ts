export interface DayBucket {
  date: string; // "2026-07-01" — DateOnly serialized by .NET
  total: number;
  failed: number;
}

export interface UserBucket {
  username: string;
  count: number;
}

export interface EntityTypeBucket {
  entityType: string;
  count: number;
}

export interface ParameterBucket {
  parameterName: string;
  count: number;
}

export interface AuditSummaryDto {
  totalActions: number;
  failedOperations: number;
  permissionChanges: number;
  byDay: DayBucket[];
  byUser: UserBucket[];
  byEntityType: EntityTypeBucket[];
  topParameters: ParameterBucket[];
}

export type DatePreset = '24h' | '7d' | '30d' | '90d' | 'custom';
