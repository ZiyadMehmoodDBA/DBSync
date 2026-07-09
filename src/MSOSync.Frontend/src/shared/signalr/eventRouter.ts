import type { QueryClient } from '@tanstack/react-query';
import { OperationsEventType, type OperationsEvent, type PermissionEvent, type ExportJobEvent } from './types';
import type { ExportJobDto } from '../types/export';
import { queryKeys } from '../queryKeys';
import { operationKeys } from '../api/operations';

export async function routeToCache(
  queryClient: QueryClient,
  event: OperationsEvent,
): Promise<void> {
  switch (event.type) {
    case OperationsEventType.NodeHealthChanged:
      return invalidateNodeHealth(queryClient);
    case OperationsEventType.NodeApproved:
    case OperationsEventType.NodeRejected:
    case OperationsEventType.NodeDisabled:
    case OperationsEventType.NodeEnabled:
      return invalidateNodeLifecycle(queryClient);
    case OperationsEventType.SyncCycleCompleted:
      return invalidateOperational(queryClient);
    case OperationsEventType.NodeLifecycleChanged:
      // Lifecycle category: nodes grid, node-management overview, topology, history, node state
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['nodes'] }),
        queryClient.invalidateQueries({ queryKey: ['node-management'] }),
        queryClient.invalidateQueries({ queryKey: ['topology-graph'] }),
        queryClient.invalidateQueries({ queryKey: ['topology-groups'] }),
        queryClient.invalidateQueries({ queryKey: ['node-state', event.nodeId] }),
        queryClient.invalidateQueries({ queryKey: ['node-transitions', event.nodeId] }),
        queryClient.invalidateQueries({ queryKey: ['node-lifecycle-history', event.nodeId] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
      ]);
      return;
    case OperationsEventType.NodeMaintenanceChanged:
      // Maintenance category: same minus history
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['nodes'] }),
        queryClient.invalidateQueries({ queryKey: ['node-management'] }),
        queryClient.invalidateQueries({ queryKey: ['topology-graph'] }),
        queryClient.invalidateQueries({ queryKey: ['node-state', event.nodeId] }),
        queryClient.invalidateQueries({ queryKey: ['node-transitions', event.nodeId] }),
      ]);
      return;
    case OperationsEventType.ConfigurationChanged:
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.nodeConfiguration(event.nodeId) }),
        queryClient.invalidateQueries({ queryKey: ['node-configuration'] }),
        queryClient.invalidateQueries({ queryKey: queryKeys.driftSummary() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.driftNodes() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.configurationSummary() }),
      ]);
      return;
    case OperationsEventType.OperationChanged:
      await queryClient.invalidateQueries({ queryKey: operationKeys.all });
      return;
  }
}

async function invalidateNodeHealth(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['nodes'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-graph'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['metrics-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
  ]);
}

async function invalidateNodeLifecycle(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['nodes'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-graph'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-groups'] }),
    queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['metrics-summary'] }),
  ]);
}

async function invalidateOperational(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['events'] }),
    queryClient.invalidateQueries({ queryKey: ['incoming-batches'] }),
    queryClient.invalidateQueries({ queryKey: ['outgoing-batches'] }),
    queryClient.invalidateQueries({ queryKey: ['batch-errors'] }),
    queryClient.invalidateQueries({ queryKey: ['metrics-summary'] }),
  ]);
}

export async function routePermissionEvent(
  queryClient: QueryClient,
  _event: PermissionEvent,
): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['permissions'] }),
    queryClient.invalidateQueries({ queryKey: ['roles'] }),
  ]);
}

export async function routeExportJobEvent(
  queryClient: QueryClient,
  event: ExportJobEvent,
): Promise<void> {
  const terminalStatuses = ['Completed', 'Failed', 'Deleted', 'Expired'];

  if (terminalStatuses.includes(event.status)) {
    await queryClient.invalidateQueries({ queryKey: ['export-jobs'] });
    return;
  }

  // Patch progress in-place for Running status (smooth progress bar)
  queryClient.setQueryData<ExportJobDto[]>(['export-jobs'], (old) => {
    if (!old) return old;
    return old.map((job) =>
      job.jobId === event.jobId
        ? { ...job, status: event.status as ExportJobDto['status'], progressPercent: event.progressPercent, rowCount: event.rowCount }
        : job
    );
  });
}
