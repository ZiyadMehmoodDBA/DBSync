export interface MetricsSummaryDto {
  totalNodes: number;
  reachableNodes: number;
  degradedNodes: number;
  unreachableNodes: number;
  unknownNodes: number;
  incomingQueueDepth: number;
  outgoingQueueDepth: number;
  batchesProcessed24h: number;
  errors24h: number;
  errorRatePercent: number;
  throughputPerMinute: number;
  generatedAt: string;
}

export interface NodeMetricsDto {
  nodeId: string;
  name: string;
  status: string;
  pendingEvents: number;
  batchesSent: number;
  batchesReceived: number;
  lastHeartbeat?: string;
  probeLatencyMs?: number;
}

export interface ChannelMetricsDto {
  channelId: string;
  name: string;
  queueDepth: number;
  throughputPerMinute: number;
  errorRate: number;
}

export interface RuntimeMetricsDto {
  uptimeSeconds: number;
  memoryMb: number;
  cpuPercent: number;
  activeWorkers: number;
  generatedAt: string;
}
