import { useEffect, useRef, useState, useCallback } from 'react';
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import type { QueryClient } from '@tanstack/react-query';
import type { ConnectionState, OperationsEvent } from './types';
import { RECONNECT_DELAYS } from './types';

interface UseSignalROptions {
  getAccessToken: () => string | null;
  isAuthenticated: boolean;
  queryClient: QueryClient;
  onEvent: (event: OperationsEvent) => void;
}

export function useSignalR({
  getAccessToken,
  isAuthenticated,
  queryClient,
  onEvent,
}: UseSignalROptions) {
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const [lastConnectedAt, setLastConnectedAt] = useState<Date | undefined>();
  const [lastDisconnectedAt, setLastDisconnectedAt] = useState<Date | undefined>();
  const getAccessTokenRef = useRef(getAccessToken);
  useEffect(() => { getAccessTokenRef.current = getAccessToken; }, [getAccessToken]);
  const connectionRef = useRef<ReturnType<typeof buildConnection> | null>(null);

  function buildConnection() {
    return new HubConnectionBuilder()
      .withUrl('/hubs/operations', {
        accessTokenFactory: () => getAccessTokenRef.current() ?? '',
      })
      .withAutomaticReconnect([...RECONNECT_DELAYS])
      .build();
  }

  const startConnection = useCallback(async () => {
    if (connectionRef.current) return;

    const conn = buildConnection();
    connectionRef.current = conn;

    conn.onreconnecting(() => {
      setConnectionState('reconnecting');
      setLastDisconnectedAt(new Date());
    });

    conn.onreconnected(async () => {
      setConnectionState('connected');
      setLastConnectedAt(new Date());
      await queryClient.invalidateQueries();
    });

    conn.onclose(() => {
      setConnectionState('disconnected');
      setLastDisconnectedAt(new Date());
      connectionRef.current = null;
    });

    conn.on('OperationsEvent', (event: OperationsEvent) => {
      onEvent(event);
    });

    try {
      await conn.start();
      setConnectionState('connected');
      setLastConnectedAt(new Date());
    } catch {
      setConnectionState('disconnected');
      connectionRef.current = null;
    }
  }, [queryClient, onEvent]);

  const stopConnection = useCallback(async () => {
    if (!connectionRef.current) return;
    const conn = connectionRef.current;
    connectionRef.current = null;
    if (conn.state !== HubConnectionState.Disconnected) {
      await conn.stop();
    }
    setConnectionState('disconnected');
    setLastDisconnectedAt(new Date());
  }, []);

  useEffect(() => {
    if (isAuthenticated) {
      void startConnection();
    } else {
      void stopConnection();
    }

    return () => {
      void stopConnection();
    };
  }, [isAuthenticated, startConnection, stopConnection]);

  return { connectionState, lastConnectedAt, lastDisconnectedAt };
}
