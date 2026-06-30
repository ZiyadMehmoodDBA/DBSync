export interface DashboardSummaryDto {
  totalNodes: number;
  reachableNodes: number;
  degradedNodes: number;
  unreachableNodes: number;
  unknownNodes: number;
  pendingEvents: number;
  queueDepth: number;
  eventsToday: number;
  transportErrors24h: number;
  generatedAt: string;
}

export interface ActivityItemDto {
  activityId: number;
  type: string;
  description: string;
  nodeId?: string;
  createTime: string;
}
