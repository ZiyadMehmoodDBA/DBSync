import { cn } from '../../lib/utils';
import { formatDistanceToNow } from 'date-fns';
import type { NotificationDto, NotificationSeverity } from './types';
import { getTargetRoute } from './routing';
import { useNavigate } from 'react-router-dom';

interface Props {
  notification: NotificationDto;
  onMarkRead:   (id: number) => void;
}

const severityBar: Record<NotificationSeverity, string> = {
  Info:     'bg-blue-400',
  Warning:  'bg-yellow-400',
  Critical: 'bg-red-500',
  Security: 'bg-purple-500',
};

export function NotificationItem({ notification, onMarkRead }: Props) {
  const navigate = useNavigate();
  const route    = getTargetRoute(notification.sourceEntityType, notification.sourceEntityId);

  function handleClick() {
    if (!notification.isRead) onMarkRead(notification.notificationId);
    if (route) void navigate(route);
  }

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={handleClick}
      onKeyDown={(e) => e.key === 'Enter' && handleClick()}
      className={cn(
        'flex gap-3 px-4 py-3 cursor-pointer hover:bg-neutral-50 dark:hover:bg-neutral-800/50',
        !notification.isRead && 'bg-blue-50/50 dark:bg-blue-950/20',
      )}
    >
      <div className={cn('mt-1.5 w-1 self-stretch rounded-full flex-shrink-0', severityBar[notification.severity])} />
      <div className="flex-1 min-w-0">
        <p className={cn('text-sm truncate', !notification.isRead && 'font-semibold')}>
          {notification.title}
          {notification.occurrenceCount > 1 && (
            <span className="ml-1.5 text-xs text-neutral-500">×{notification.occurrenceCount}</span>
          )}
        </p>
        <p className="text-xs text-neutral-500 truncate mt-0.5">
          {notification.body.slice(0, 120)}
        </p>
        <p className="text-xs text-neutral-400 mt-1">
          {formatDistanceToNow(new Date(notification.createdAt), { addSuffix: true })}
        </p>
      </div>
      {!notification.isRead && (
        <div className="mt-2 h-2 w-2 rounded-full bg-blue-500 flex-shrink-0" />
      )}
    </div>
  );
}
