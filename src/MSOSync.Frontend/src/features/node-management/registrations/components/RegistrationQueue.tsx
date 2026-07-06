import { useQueryClient } from '@tanstack/react-query';
import { useNodeManagement } from '../../NodeManagementProvider';
import { useNodeManagementRegistrations } from '../../hooks/useNodeManagementRegistrations';
import { nodeManagementKeys } from '../../hooks/queryKeys';
import { getRegistrationDetail } from '../../api/nodeManagementApi';
import { cn } from '../../../../lib/utils';
import type { RegistrationStatus } from '../../types/registration';

const STATUS_COLORS: Record<RegistrationStatus, string> = {
  Pending:  'bg-amber-100 text-amber-700 dark:bg-amber-900/50 dark:text-amber-300',
  Approved: 'bg-green-100 text-green-700 dark:bg-green-900/50 dark:text-green-300',
  Rejected: 'bg-red-100   text-red-700   dark:bg-red-900/50   dark:text-red-300',
};

export function RegistrationQueue() {
  const qc = useQueryClient();
  const {
    selectedRegistration,
    setSelectedRegistration,
    toggleBulkSelection,
    bulkSelection,
  } = useNodeManagement();

  const { data, isLoading, isError } = useNodeManagementRegistrations({
    status:            'Pending',
    includeTotalCount: true,
    pageSize:          100,
  });

  function handleHover(id: number) {
    void qc.prefetchQuery({
      queryKey: nodeManagementKeys.registrationDetail(id),
      queryFn:  () => getRegistrationDetail(id),
      staleTime: 60_000,
    });
  }

  if (isLoading) return <div className="p-4 text-sm text-neutral-400">Loading…</div>;
  if (isError)   return <div className="p-4 text-sm text-red-500">Failed to load registrations.</div>;

  const items = data?.items ?? [];

  return (
    <div className="flex flex-col h-full overflow-y-auto">
      <div className="px-3 py-2 text-xs text-neutral-500 border-b dark:border-neutral-800">
        {data?.totalCount ?? items.length} pending
      </div>
      {items.map(r => (
        <div
          key={r.id}
          onMouseEnter={() => handleHover(r.id)}
          onClick={() => setSelectedRegistration(r)}
          className={cn(
            'flex items-start gap-2 px-3 py-3 cursor-pointer border-b dark:border-neutral-800 transition-colors',
            selectedRegistration?.id === r.id
              ? 'bg-blue-50 dark:bg-blue-950/20'
              : 'hover:bg-neutral-50 dark:hover:bg-neutral-800/50',
          )}
        >
          <input
            type="checkbox"
            checked={bulkSelection.has(r.id)}
            onChange={() => toggleBulkSelection(r.id)}
            onClick={e => e.stopPropagation()}
            className="mt-0.5 shrink-0"
          />
          <div className="flex-1 min-w-0">
            <p className="font-medium text-sm truncate">{r.nodeName}</p>
            <p className="text-xs text-neutral-500 truncate">{r.nodeExternalId}</p>
            <div className="flex items-center gap-2 mt-1">
              <span className={cn('rounded px-1.5 py-0.5 text-xs font-medium', STATUS_COLORS[r.status])}>
                {r.status}
              </span>
              <span className="text-xs text-neutral-400">{r.registrationType}</span>
            </div>
          </div>
        </div>
      ))}
      {items.length === 0 && (
        <div className="p-4 text-center text-sm text-neutral-400">No pending registrations.</div>
      )}
    </div>
  );
}
