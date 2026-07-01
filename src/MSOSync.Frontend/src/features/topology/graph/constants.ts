export const GROUP_NODE_WIDTH  = 220;
export const GROUP_NODE_HEIGHT = 100;

export const ConnectivityStatus = {
  Unknown:     0,
  Reachable:   1,
  Degraded:    2,
  Unreachable: 3,
} as const;

export type ConnectivityStatusValue =
  typeof ConnectivityStatus[keyof typeof ConnectivityStatus];

export const CONNECTIVITY_META: Record<number, { label: string; dot: string }> = {
  [ConnectivityStatus.Unknown]:     { label: 'Unknown',     dot: 'bg-gray-400'  },
  [ConnectivityStatus.Reachable]:   { label: 'Reachable',   dot: 'bg-green-500' },
  [ConnectivityStatus.Degraded]:    { label: 'Degraded',    dot: 'bg-amber-400' },
  [ConnectivityStatus.Unreachable]: { label: 'Unreachable', dot: 'bg-red-500'   },
};
