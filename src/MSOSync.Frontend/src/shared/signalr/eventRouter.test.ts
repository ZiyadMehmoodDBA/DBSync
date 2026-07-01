import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient } from '@tanstack/react-query';
import { routeToCache } from './eventRouter';
import { OperationsEventType, type OperationsEvent } from './types';

function makeEvent(type: OperationsEventType): OperationsEvent {
  return {
    type,
    nodeId: 'node-1',
    nodeLabel: null,
    previousStatus: null,
    currentStatus: null,
    occurredAt: new Date().toISOString(),
    groupId: null,
  };
}

describe('routeToCache', () => {
  let queryClient: QueryClient;
  let invalidateSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    queryClient = new QueryClient();
    invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
  });

  it('NodeHealthChanged invalidates nodes, topology-graph, topology-summary, metrics-summary, dashboard-summary', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.NodeHealthChanged));

    const keys = invalidateSpy.mock.calls.map((c: unknown[]) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toContain('nodes');
    expect(keys).toContain('topology-graph');
    expect(keys).toContain('topology-summary');
    expect(keys).toContain('metrics-summary');
    expect(keys).toContain('dashboard-summary');
    expect(keys).toHaveLength(5);
  });

  it('NodeApproved invalidates nodes, topology-graph, topology-groups, dashboard-summary, metrics-summary', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.NodeApproved));

    const keys = invalidateSpy.mock.calls.map((c: unknown[]) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toContain('nodes');
    expect(keys).toContain('topology-graph');
    expect(keys).toContain('topology-groups');
    expect(keys).toContain('dashboard-summary');
    expect(keys).toContain('metrics-summary');
    expect(keys).toHaveLength(5);
  });

  it('NodeRejected uses same invalidation group as NodeApproved', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.NodeRejected));

    const keys = invalidateSpy.mock.calls.map((c: unknown[]) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toHaveLength(5);
    expect(keys).toContain('nodes');
  });

  it('NodeDisabled uses same invalidation group as NodeApproved', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.NodeDisabled));

    const keys = invalidateSpy.mock.calls.map((c: unknown[]) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toHaveLength(5);
    expect(keys).toContain('nodes');
  });

  it('NodeEnabled uses same invalidation group as NodeApproved', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.NodeEnabled));

    const keys = invalidateSpy.mock.calls.map((c: unknown[]) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toHaveLength(5);
    expect(keys).toContain('nodes');
  });

  it('SyncCycleCompleted invalidates dashboard-summary, events, incoming-batches, outgoing-batches, batch-errors, metrics-summary', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.SyncCycleCompleted));

    const keys = invalidateSpy.mock.calls.map((c: unknown[]) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toContain('dashboard-summary');
    expect(keys).toContain('events');
    expect(keys).toContain('incoming-batches');
    expect(keys).toContain('outgoing-batches');
    expect(keys).toContain('batch-errors');
    expect(keys).toContain('metrics-summary');
    expect(keys).toHaveLength(6);
  });
});
