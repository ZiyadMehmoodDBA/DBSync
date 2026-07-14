import { useState } from 'react';
import { Bell } from 'lucide-react';
import { Button } from '../../components/ui/button';
import { NotificationItem } from './NotificationItem';
import { useNotifications, useMarkRead, useMarkAllRead, useUnreadCount } from './hooks';

const FILTERS = ['all', 'unread', 'critical', 'security'] as const;
type Filter = (typeof FILTERS)[number];

export function NotificationsPage() {
  const [filter, setFilter]  = useState<Filter>('all');
  const unreadCount          = useUnreadCount();
  const { items, loadMore, hasMore, isLoading } = useNotifications(filter, 20);
  const markRead    = useMarkRead();
  const markAllRead = useMarkAllRead();

  return (
    <div className="p-6 max-w-2xl">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-semibold flex items-center gap-2">
          <Bell className="h-6 w-6" />
          Notifications
        </h1>
        <Button
          variant="outline"
          size="sm"
          disabled={unreadCount === 0 || markAllRead.isPending}
          onClick={() => void markAllRead.mutateAsync()}
        >
          Mark all read
        </Button>
      </div>

      {/* Filter tabs */}
      <div className="flex gap-1 mb-4 border-b border-neutral-200 dark:border-neutral-800">
        {FILTERS.map(f => (
          <button
            key={f}
            onClick={() => setFilter(f)}
            className={[
              'px-3 py-2 text-sm capitalize border-b-2 -mb-px transition-colors',
              filter === f
                ? 'border-blue-500 text-blue-600 font-medium'
                : 'border-transparent text-neutral-500 hover:text-neutral-800 dark:hover:text-neutral-200',
            ].join(' ')}
          >
            {f}
          </button>
        ))}
      </div>

      {/* List */}
      <div className="rounded-lg border border-neutral-200 dark:border-neutral-800 divide-y divide-neutral-100 dark:divide-neutral-800 overflow-hidden">
        {isLoading && (
          <p className="px-4 py-8 text-sm text-center text-neutral-500">Loading…</p>
        )}
        {!isLoading && items.length === 0 && (
          <div className="flex flex-col items-center gap-2 py-16 text-neutral-400">
            <Bell className="h-8 w-8" />
            <p className="text-sm">No notifications</p>
          </div>
        )}
        {items.map(n => (
          <NotificationItem
            key={n.notificationId}
            notification={n}
            onMarkRead={(id) => void markRead.mutateAsync({ notificationId: id })}
          />
        ))}
      </div>

      {hasMore && (
        <div className="mt-4 text-center">
          <Button variant="outline" size="sm" onClick={() => void loadMore()}>
            Load more
          </Button>
        </div>
      )}
    </div>
  );
}
