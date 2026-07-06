export type RegistrationType  = 'New' | 'ReRegistration' | 'Recovery';
export type RegistrationStatus = 'Pending' | 'Approved' | 'Rejected';
export type RegistrationChangeType = 'Unchanged' | 'Added' | 'Modified' | 'Removed';

export interface RegistrationSummaryDto {
  id:               number;
  nodeExternalId:   string;
  nodeName:         string;
  registrationType: RegistrationType;
  status:           RegistrationStatus;
  receivedAt:       string;
  processedAt:      string | null;
  processedBy:      string | null;
}

export interface RegistrationDiffItemDto {
  field:         string;
  currentValue:  string | null;
  incomingValue: string | null;
  changeType:    RegistrationChangeType;
}

export interface RegistrationDiffDto {
  items: RegistrationDiffItemDto[];
}

export interface MachineMetadata {
  hostName?:    string;
  osVersion?:   string;
  machineName?: string;
}

export interface DatabaseMetadata {
  edition?:      string;
  version?:      string;
  collation?:    string;
  instanceName?: string;
}

export interface ApplicationMetadata {
  agentVersion?:   string;
  runtimeVersion?: string;
  installPath?:    string;
}

export interface HardwareMetadata {
  cpuCount?:  number;
  ramBytes?:  number;
  diskBytes?: number;
}

export interface RegistrationMetadataDto {
  schemaVersion: number;
  machine?:      MachineMetadata;
  database?:     DatabaseMetadata;
  application?:  ApplicationMetadata;
  hardware?:     HardwareMetadata;
}

export interface RegistrationDetailDto extends RegistrationSummaryDto {
  metadata: RegistrationMetadataDto | null;
  diff:     RegistrationDiffDto | null;
}

export interface RegistrationListFilter {
  status?:            RegistrationStatus;
  registrationType?:  RegistrationType;
  pageSize?:          number;
  cursor?:            string;
  includeTotalCount?: boolean;
}

/** Alias used by useInfiniteQuery cursor pagination hooks. */
export type RegistrationFilter = RegistrationListFilter;

export interface CursorPageResult<T> {
  items:       T[];
  nextCursor:  string | null;
  totalCount?: number;
}
