import { useCallback, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../features/auth/useAuth';
import { SignalRContext } from './context';
import { useSignalR } from './useSignalR';
import type { OperationsEvent } from './types';

interface Props {
  children: ReactNode;
}

export function SignalRProvider({ children }: Props) {
  const { accessToken } = useAuth();
  const queryClient = useQueryClient();

  const getAccessToken = useCallback(() => accessToken, [accessToken]);

  const handleEvent = useCallback((_event: OperationsEvent) => {
    // Routing wired in Task 3 — placeholder keeps the callback stable
  }, []);

  const { connectionState, lastConnectedAt, lastDisconnectedAt } = useSignalR({
    getAccessToken,
    isAuthenticated: accessToken !== null,
    queryClient,
    onEvent: handleEvent,
  });

  return (
    <SignalRContext.Provider value={{ connectionState, lastConnectedAt, lastDisconnectedAt }}>
      {children}
    </SignalRContext.Provider>
  );
}
