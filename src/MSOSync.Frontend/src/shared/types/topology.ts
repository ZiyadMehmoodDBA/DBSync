export interface TopologySummaryDto {
  totalGroups: number;
  totalNodes: number;
  reachableNodes: number;
  degradedNodes: number;
  unreachableNodes: number;
  unknownNodes: number;
  generatedAt: string;
}

export interface TopologyGroupDto {
  groupId: string;
  name: string;
  totalNodes: number;
  reachableNodes: number;
  degradedNodes: number;
  unreachableNodes: number;
  unknownNodes: number;
  connectivityStatus: string;
}

export interface TopologyGroupNodeDto {
  nodeId: string;
  name: string;
  status: string;
  lastHeartbeat?: string;
  probeLatencyMs?: number;
}
