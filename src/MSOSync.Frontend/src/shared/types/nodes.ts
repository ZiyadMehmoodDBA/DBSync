export interface NodeDto {
  nodeId: string;
  groupId: string;
  syncUrl: string;
  status: string;
  registrationTime?: string;
  lastHeartbeat?: string;
  heartbeatInterval: number;
  syncEnabled: boolean;
  transportMode: 'Pull' | 'Push';
  dbServer?: string;
  dbName?: string;
  dbAuthMode?: string;
  dbUser?: string;
  hasDbPassword: boolean;
}
