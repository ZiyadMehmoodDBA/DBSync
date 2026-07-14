export type SyncDirection = 'NodeToHub' | 'HubToNode' | 'Bidirectional';
export type InitialLoadPolicy = 'None' | 'ChangesOnly' | 'FullLoad';

export interface NodeScopeDto {
  nodeId: string;
  syncDirection: SyncDirection;
  initialLoadPolicy: InitialLoadPolicy;
  channelIds: string[];
  triggerIds: string[];
  routerIds: string[];
}

export interface SetNodeScopeRequest {
  syncDirection: SyncDirection;
  initialLoadPolicy: InitialLoadPolicy;
  channelIds: string[];
  triggerIds: string[];
  routerIds: string[];
}
