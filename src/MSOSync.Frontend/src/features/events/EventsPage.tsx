import { useState } from 'react';
import type { EventFilter } from '../../shared/types';
import { EventFilters } from './EventFilters';
import { EventsGrid } from './EventsGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';
import { useEvents } from './hooks';

const defaultFilter: EventFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE };

export function EventsPage() {
  const [filter, setFilter] = useState<EventFilter>(defaultFilter);
  const { data } = useEvents(filter);

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Events</h1>
        <ExportMenu
          resource="events"
          currentData={(data?.data ?? []) as unknown as Record<string, unknown>[]}
          queryParams={filter as unknown as Record<string, string | number | boolean | undefined>}
        />
      </div>
      <EventFilters onFilter={setFilter} />
      <EventsGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
