import type { ConnectivityStatusName, NodeLifecycleState } from './lifecycle';

export interface NodeDto {
  nodeId: string;
  groupId: string;
  syncUrl: string;
  lifecycleState: NodeLifecycleState;
  registrationTime?: string;
  lastHeartbeat?: string;
  heartbeatInterval: number;
  canSynchronize: boolean;
  transportMode: 'Pull' | 'Push';
  connectivityStatus: ConnectivityStatusName;
  maintenanceMode: boolean;
  dbServer?: string;
  dbName?: string;
  dbAuthMode?: string;
  dbUser?: string;
  hasDbPassword: boolean;
}
