export type RollingKind = 'RollingMaintenance' | 'RollingUpgrade';
export type WaveAction = 'manual-confirm' | 'auto-window';
export type RollingStepStatus =
  | 'Pending' | 'Draining' | 'InMaintenance' | 'AwaitingVerification'
  | 'Completed' | 'Failed' | 'Skipped';

export interface CreateRollingOperationRequest {
  kind: RollingKind;
  nodeIds: string[];
  waveSize?: number;
  wavePercent?: number;
  gateSoakSeconds: number;
  waveAction: WaveAction;
  windowSeconds?: number;
  targetVersion?: string;
  verificationTimeoutSeconds: number;
}

export interface RollingOperationPolicy {
  waveSize: number | null;
  wavePercent: number | null;
  gateSoakSeconds: number;
  waveAction: WaveAction;
  windowSeconds: number | null;
  targetVersion: string | null;
  verificationTimeoutSeconds: number;
}

export interface RollingStepDto {
  stepId: string;
  nodeId: string;
  waveNumber: number;
  status: RollingStepStatus;
  startedAt: string | null;
  completedAt: string | null;
  errorMessage: string | null;
}

export interface RollingOperationDetailDto {
  operationId: string;
  operationType: string;
  status: string;
  result: string | null;
  policy: RollingOperationPolicy;
  steps: RollingStepDto[];
}
