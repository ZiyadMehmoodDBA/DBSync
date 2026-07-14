export function getTargetRoute(
  entityType: string | null | undefined,
  entityId:   string | null | undefined,
): string | null {
  switch (entityType) {
    case 'Node':      return entityId ? `/operations/nodes/${entityId}` : '/operations/nodes';
    case 'Worker':    return entityId ? `/operations/workers/${entityId}` : '/operations/workers';
    case 'Operation': return entityId ? `/operations/${entityId}` : '/operations';
    default:          return null;
  }
}
