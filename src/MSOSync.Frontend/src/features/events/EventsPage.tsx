import { useState } from 'react';
import type { EventFilter } from '../../shared/types';
import { EventFilters } from './EventFilters';
import { EventsGrid } from './EventsGrid';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';

const defaultFilter: EventFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE };

export function EventsPage() {
  const [filter, setFilter] = useState<EventFilter>(defaultFilter);

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Events</h1>
      <EventFilters onFilter={setFilter} />
      <EventsGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
