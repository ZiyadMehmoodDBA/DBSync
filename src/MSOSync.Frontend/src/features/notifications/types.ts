export type NotificationEventType =
  | 'WorkerFailed'
  | 'WorkerWarning'
  | 'NodeUnreachable'
  | 'NodeInRecovery'
  | 'NodeRejected'
  | 'NodeDecommissioned'
  | 'SchedulerRecovered'
  | 'AccountLocked'
  | 'TokenReuseDetected'
  | 'OperationFailed';

export type NotificationSeverity = 'Info' | 'Warning' | 'Critical' | 'Security';

export interface NotificationDto {
  notificationId:   number;
  eventType:        NotificationEventType;
  severity:         NotificationSeverity;
  title:            string;
  body:             string;
  sourceEntityType: string | null;
  sourceEntityId:   string | null;
  correlationId:    string | null;
  createdAt:        string; // ISO 8601
  lastOccurredAt:   string;
  occurrenceCount:  number;
  isRead:           boolean;
  readAt:           string | null;
}

export interface NotificationPageDto {
  items:       NotificationDto[];
  nextCursor:  string | null;
  totalUnread: number;
}

export interface NotificationPushPayload {
  notificationId:   number;
  eventType:        NotificationEventType;
  severity:         NotificationSeverity;
  title:            string;
  body:             string;
  sourceEntityType: string | null;
  sourceEntityId:   string | null;
  createdAt:        string;
  unreadCount:      number;
}
