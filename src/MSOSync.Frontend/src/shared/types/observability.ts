export interface NodeHealthScore {
  nodeId: string;
  nodeName: string;
  score: number;
  grade: 'A' | 'B' | 'C' | 'D' | 'F';
  connectivityScore: number;
  syncLagScore: number;
  errorRateScore: number;
  heartbeatScore: number;
  computedAt: string;
}

export interface SloStatus {
  deliveryRate: number;
  deliveryRateTarget: number;
  deliveryRateMet: boolean;
  latencyP99Ms: number;
  latencyP99TargetMs: number;
  latencyP99Met: boolean;
  windowStart: string;
  windowEnd: string;
}
