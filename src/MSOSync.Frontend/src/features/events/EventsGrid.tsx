import type { EventFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { eventColumns } from './columns';
import { useEvents } from './hooks';

interface Props {
  filter: EventFilter;
  onFilterChange: (f: EventFilter) => void;
}

export function EventsGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useEvents(filter);

  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={eventColumns}
      loading={isLoading}
      total={data?.total ?? 0}
      page={filter.page}
      pageSize={filter.pageSize}
      onPageChange={(p) => onFilterChange({ ...filter, page: p })}
      onPageSizeChange={(s) => onFilterChange({ ...filter, page: 1, pageSize: s })}
      error={error}
      onRetry={() => void refetch()}
      height={500}
    />
  );
}
