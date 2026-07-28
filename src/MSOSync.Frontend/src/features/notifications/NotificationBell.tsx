import { useState } from 'react';
import { Bell, Store } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Button } from '../../components/ui/button';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '../../components/ui/popover';
import { NotificationItem } from './NotificationItem';
import { useUnreadCount, useNotifications, useMarkRead, useMarkAllRead } from './hooks';
import { useUpdateCount } from '../../shared/hooks/useMarketplace';

export function NotificationBell() {
  const [open, setOpen]      = useState(false);
  const unreadCount          = useUnreadCount();
  const { items, isLoading } = useNotifications('all', 5);
  const markRead             = useMarkRead();
  const markAllRead          = useMarkAllRead();
  const updateCount          = useUpdateCount();

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button variant="ghost" size="icon" className="relative" aria-label="Notifications">
          <Bell className="h-5 w-5" />
          {unreadCount > 0 && (
            <span className="absolute -top-0.5 -right-0.5 flex h-4 w-4 items-center justify-center rounded-full bg-red-500 text-[10px] font-bold text-white">
              {unreadCount > 99 ? '99+' : unreadCount}
            </span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80 p-0">
        <div className="flex items-center justify-between px-4 py-3 border-b border-neutral-200 dark:border-neutral-800">
          <span className="text-sm font-semibold">Notifications</span>
          {unreadCount > 0 && (
            <Button
              variant="ghost"
              size="sm"
              className="text-xs h-7"
              onClick={() => void markAllRead.mutateAsync()}
              disabled={markAllRead.isPending}
            >
              Mark all read
            </Button>
          )}
        </div>
        <div className="divide-y divide-neutral-100 dark:divide-neutral-800 max-h-80 overflow-y-auto">
          {updateCount > 0 && (
            <div className="flex items-center gap-3 px-4 py-3 bg-blue-50 dark:bg-blue-900/20">
              <Store className="h-4 w-4 text-blue-600 dark:text-blue-400 shrink-0" />
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-blue-900 dark:text-blue-100">
                  {updateCount} plugin {updateCount === 1 ? 'update' : 'updates'} available
                </p>
                <Link
                  to="/marketplace"
                  onClick={() => setOpen(false)}
                  className="text-xs text-blue-600 hover:underline dark:text-blue-400"
                >
                  View updates →
                </Link>
              </div>
            </div>
          )}
          {isLoading && (
            <p className="px-4 py-6 text-sm text-center text-neutral-500">Loading…</p>
          )}
          {!isLoading && items.length === 0 && (
            <p className="px-4 py-6 text-sm text-center text-neutral-500">No notifications</p>
          )}
          {items.map(n => (
            <NotificationItem
              key={n.notificationId}
              notification={n}
              onMarkRead={(id) => void markRead.mutateAsync({ notificationId: id })}
            />
          ))}
        </div>
        <div className="border-t border-neutral-200 dark:border-neutral-800 px-4 py-2">
          <Link
            to="/notifications"
            onClick={() => setOpen(false)}
            className="text-xs text-blue-600 hover:underline dark:text-blue-400"
          >
            View all notifications →
          </Link>
        </div>
      </PopoverContent>
    </Popover>
  );
}
