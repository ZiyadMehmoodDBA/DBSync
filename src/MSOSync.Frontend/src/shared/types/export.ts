export const ExportJobStatus = {
  Pending:   'Pending',
  Running:   'Running',
  Completed: 'Completed',
  Failed:    'Failed',
  Deleted:   'Deleted',
  Expired:   'Expired',
} as const;

export type ExportJobStatus = (typeof ExportJobStatus)[keyof typeof ExportJobStatus];

export interface ExportJobDto {
  jobId:           string;
  parentJobId:     string | null;
  requestedBy:     string;
  resourceType:    string;
  format:          string;
  filtersJson:     string;
  status:          ExportJobStatus;
  progressPercent: number;
  rowCount:        number | null;
  errorMessage:    string | null;
  expiresAt:       string | null;
  createdAt:       string;
  startedAt:       string | null;
  completedAt:     string | null;
}

export interface CreateExportJobRequest {
  resourceType: string;
  format:       string;
  filtersJson:  string;
  parentJobId?: string;
}
