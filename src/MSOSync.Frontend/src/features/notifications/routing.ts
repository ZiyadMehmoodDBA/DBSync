export function getTargetRoute(
  entityType: string | null | undefined,
  entityId:   string | null | undefined,
): string | null {
  switch (entityType) {
    case 'Node':      return entityId ? `/operations/nodes` : '/operations/nodes';
    case 'Worker':    return '/operations/health';
    case 'Operation': return entityId ? `/operations/jobs` : '/operations/jobs';
    default:          return null;
  }
}
