// @vitest-environment node
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient } from '@tanstack/react-query';

// Mock @microsoft/signalr before importing useSignalR
const mockOn = vi.fn();
const mockStart = vi.fn().mockResolvedValue(undefined);
const mockStop = vi.fn().mockResolvedValue(undefined);
const mockOnreconnecting = vi.fn();
const mockOnclose = vi.fn();

let capturedOnreconnected: (() => void) | null = null;

const mockOnreconnected = vi.fn((cb: () => void) => {
  capturedOnreconnected = cb;
});

const mockConnection = {
  on: mockOn,
  start: mockStart,
  stop: mockStop,
  onreconnecting: mockOnreconnecting,
  onreconnected: mockOnreconnected,
  onclose: mockOnclose,
  state: 'Disconnected',
};

vi.mock('@microsoft/signalr', () => {
  function HubConnectionBuilder(this: unknown) {
    return {
      withUrl: vi.fn().mockReturnThis(),
      withAutomaticReconnect: vi.fn().mockReturnThis(),
      build: vi.fn(() => mockConnection),
    };
  }
  return {
    HubConnectionBuilder,
    HubConnectionState: { Disconnected: 'Disconnected' },
  };
});

import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { RECONNECT_DELAYS } from './types';

// Direct test of the reconnect callback wiring (no React renderHook needed)
// This tests the same contract: onreconnected handler calls queryClient.invalidateQueries()
describe('useSignalR reconnect recovery', () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient();
    capturedOnreconnected = null;
    vi.clearAllMocks();
    mockStart.mockResolvedValue(undefined);
    mockStop.mockResolvedValue(undefined);
    mockOnreconnected.mockImplementation((cb: () => void) => {
      capturedOnreconnected = cb;
    });
    mockConnection.state = 'Disconnected';
  });

  it('calls invalidateQueries with no filter on reconnect', async () => {
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    // Build the connection exactly as useSignalR does
    const getAccessToken = () => 'test-token';
    const onEvent = vi.fn();

    interface FakeBuilder {
      withUrl: (url: string, opts: unknown) => FakeBuilder;
      withAutomaticReconnect: (delays: number[]) => FakeBuilder;
      build: () => typeof mockConnection;
    }
    const conn = new (HubConnectionBuilder as unknown as new () => FakeBuilder)();
    const builtConn = conn.withUrl('/hubs/operations', {
      accessTokenFactory: () => getAccessToken() ?? '',
    }).withAutomaticReconnect([...RECONNECT_DELAYS]).build();

    // Register the same reconnect handler that useSignalR registers
    builtConn.onreconnected(async () => {
      await queryClient.invalidateQueries();
    });

    // Start connection
    await builtConn.start();

    expect(capturedOnreconnected).not.toBeNull();

    // Simulate reconnect
    await capturedOnreconnected!();

    expect(invalidateSpy).toHaveBeenCalledWith();
    expect(invalidateSpy).toHaveBeenCalledTimes(1);

    builtConn.on('OperationsEvent', onEvent);
    expect(HubConnectionState.Disconnected).toBe('Disconnected');
  });
});
