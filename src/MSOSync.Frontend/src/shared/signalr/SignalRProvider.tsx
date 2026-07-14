import { useCallback, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { useAuth } from '../../features/auth/useAuth';
import { SignalRContext } from './context';
import { useSignalR } from './useSignalR';
import { routeToCache, routePermissionEvent, routeExportJobEvent } from './eventRouter';
import { routeToToast } from './notifications';
import type { OperationsEvent, PermissionEvent, ExportJobEvent, NotificationPushPayload } from './types';
import { systemKeys } from '../api/system';
import { queryKeys } from '../queryKeys';

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

  const handleOverviewRefreshed = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: systemKeys.overview });
  }, [queryClient]);

  const handleNotificationEvent = useCallback(
    (payload: NotificationPushPayload) => {
      queryClient.setQueryData(queryKeys.notificationsUnread(), payload.unreadCount);
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      toast.info(payload.title, { description: payload.body.slice(0, 80) });
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
    onOverviewRefreshed: handleOverviewRefreshed,
    onNotificationEvent: handleNotificationEvent,
  });

  return (
    <SignalRContext.Provider value={{ connectionState, lastConnectedAt, lastDisconnectedAt }}>
      {children}
    </SignalRContext.Provider>
  );
}
