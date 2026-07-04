import { useCallback, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../features/auth/useAuth';
import { SignalRContext } from './context';
import { useSignalR } from './useSignalR';
import { routeToCache, routePermissionEvent, routeExportJobEvent } from './eventRouter';
import { routeToToast } from './notifications';
import type { OperationsEvent, PermissionEvent, ExportJobEvent } from './types';

interface Props {
  children: ReactNode;
}

export function SignalRProvider({ children }: Props) {
  const { accessToken } = useAuth();
  const queryClient = useQueryClient();

  const getAccessToken = useCallback(() => accessToken, [accessToken]);

  const handleEvent = useCallback(
    (event: OperationsEvent) => {
      void routeToCache(queryClient, event);
      routeToToast(event);
    },
    [queryClient],
  );

  const handlePermissionEvent = useCallback(
    (event: PermissionEvent) => {
      void routePermissionEvent(queryClient, event);
    },
    [queryClient],
  );

  const handleExportJobEvent = useCallback(
    (event: ExportJobEvent) => {
      void routeExportJobEvent(queryClient, event);
    },
    [queryClient],
  );

  const { connectionState, lastConnectedAt, lastDisconnectedAt } = useSignalR({
    getAccessToken,
    isAuthenticated: accessToken !== null,
    queryClient,
    onEvent: handleEvent,
    onPermissionEvent: handlePermissionEvent,
    onExportJobEvent: handleExportJobEvent,
  });

  return (
    <SignalRContext.Provider value={{ connectionState, lastConnectedAt, lastDisconnectedAt }}>
      {children}
    </SignalRContext.Provider>
  );
}
