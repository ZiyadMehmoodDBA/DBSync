export type StatusVariant = 'success' | 'warning' | 'danger' | 'neutral';

export function nodeStatusVariant(status: string): StatusVariant {
  switch (status.toUpperCase()) {
    case 'REGISTERED': return 'success';
    case 'DEGRADED': return 'warning';
    case 'OFFLINE':
    case 'UNREACHABLE': return 'danger';
    default: return 'neutral';
  }
}

export function batchStatusVariant(status: string): StatusVariant {
  switch (status.toUpperCase()) {
    case 'OK': return 'success';
    case 'SENT': return 'neutral';
    case 'ERROR':
    case 'CONFLICT': return 'danger';
    case 'LOADING': return 'warning';
    default: return 'neutral';
  }
}

export function connectivityStatusVariant(status: string): StatusVariant {
  switch (status.toUpperCase()) {
    case 'HEALTHY': return 'success';
    case 'DEGRADED': return 'warning';
    case 'UNREACHABLE': return 'danger';
    default: return 'neutral';
  }
}
