export type ChangeType = 'Added' | 'Removed' | 'Changed' | 'Unchanged';

export interface DiffEntryDto {
  key: string;
  changeType: ChangeType;
  oldValue: string | null;
  newValue: string | null;
}

export interface ConfigVersionDiffDto {
  templateId: string;
  v1: number;
  v2: number;
  v1Label: string;
  v2Label: string;
  entries: DiffEntryDto[];
  hasChanges: boolean;
}
