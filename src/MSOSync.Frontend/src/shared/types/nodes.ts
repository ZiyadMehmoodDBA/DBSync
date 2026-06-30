export interface NodeDto {
  nodeId: string;
  groupId: string;
  name: string;
  status: string;
  syncEnabled: boolean;
  lastHeartbeat?: string;
  probeLatencyMs?: number;
  createdTime: string;
  syncUrl?: string;
  heartbeatInterval?: number;
}
