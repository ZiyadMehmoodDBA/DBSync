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

export const LIFECYCLE_META: Record<string, { label: string; border: string; icon: string }> = {
  Active:              { label: 'Active',              border: 'border-green-500',   icon: '●' },
  Recovery:            { label: 'Recovery',            border: 'border-orange-500',  icon: '◐' },
  Disabled:            { label: 'Disabled',            border: 'border-neutral-400', icon: '○' },
  Decommissioning:     { label: 'Decommissioning',     border: 'border-purple-500',  icon: '◍' },
  Decommissioned:      { label: 'Decommissioned',      border: 'border-neutral-300', icon: '◌' },
  PendingApproval:     { label: 'Pending Approval',    border: 'border-yellow-500',  icon: '◔' },
  PendingRegistration: { label: 'Pending Registration',border: 'border-blue-500',    icon: '◔' },
  Rejected:            { label: 'Rejected',            border: 'border-red-500',     icon: '✕' },
};
