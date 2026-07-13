export interface EntityChipDto {
  entityType: string;
  entityId: string;
  displayLabel: string | null;
}

export interface CorrelationEventDto {
  auditId: number;
  occurredAt: string;
  durationSincePrevious: string | null;
  actionName: string;
  summary: string;
  actorUsername: string | null;
  category: string;
  severity: string;
  entityType: string | null;
  entityId: string | null;
  deepLink: string | null;
}

export interface CorrelationPhaseDto {
  phaseName: string;
  category: string;
  events: CorrelationEventDto[];
  hasErrors: boolean;
}

export interface CorrelationTimelineDto {
  correlationId: string;
  operationType: string | null;
  operationStatus: string | null;
  operationResult: string | null;
  startedAt: string | null;
  completedAt: string | null;
  duration: string | null;
  initiatedBy: string | null;
  entityChips: EntityChipDto[];
  totalEventCount: number;
  isFailedWorkflow: boolean;
  failureSummary: string | null;
  phases: CorrelationPhaseDto[];
}

export interface CorrelationSearchResultDto {
  correlationId: string;
  eventCount: number;
  firstSeen: string;
  lastSeen: string;
  primaryEntityType: string | null;
  isFailedWorkflow: boolean;
}
