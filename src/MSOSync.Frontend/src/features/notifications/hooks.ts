import { useCallback, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getNotifications, getUnreadCount, markRead, markAllRead } from './api';
import type { NotificationDto } from './types';

export function useUnreadCount(): number {
  const { data = 0 } = useQuery({
    queryKey: queryKeys.notificationsUnread(),
    queryFn:  ({ signal }) => getUnreadCount({ signal }),
    staleTime: 60_000,
  });
  return data;
}

export function useNotifications(
  filter:   'all' | 'unread' | 'critical' | 'security' = 'all',
  pageSize  = 20,
) {
  const [cursor, setCursor]   = useState<string | null>(null);
  const [items,  setItems]    = useState<NotificationDto[]>([]);
  const [hasMore, setHasMore] = useState(false);

  const unreadOnly = filter === 'unread';
  const severity   = filter === 'critical' ? 'Critical' : filter === 'security' ? 'Security' : undefined;

  const { isLoading, isFetching } = useQuery({
    queryKey: queryKeys.notifications(filter),
    queryFn:  async ({ signal }) => {
      const page = await getNotifications(null, pageSize, unreadOnly, { signal, severity });
      setItems(page.items);
      setCursor(page.nextCursor);
      setHasMore(page.nextCursor !== null);
      return page;
    },
    staleTime: 30_000,
  });

  const loadMore = useCallback(async () => {
    if (!cursor) return;
    const page = await getNotifications(cursor, pageSize, unreadOnly, { severity });
    setItems(prev => [...prev, ...page.items]);
    setCursor(page.nextCursor);
    setHasMore(page.nextCursor !== null);
  }, [cursor, pageSize, unreadOnly, severity]);

  return { items, loadMore, hasMore, isLoading: isLoading || isFetching };
}

export function useMarkRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ notificationId }: { notificationId: number }) => markRead(notificationId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      void queryClient.invalidateQueries({ queryKey: queryKeys.notificationsUnread() });
    },
  });
}

export function useMarkAllRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: markAllRead,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      void queryClient.invalidateQueries({ queryKey: queryKeys.notificationsUnread() });
    },
  });
}
