import { useState } from 'react';
import type { AuditFilter } from '../../shared/types';
import { AuditFilters } from './AuditFilters';
import { AuditGrid } from './AuditGrid';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';

const defaultFilter: AuditFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE };

export function AuditPage() {
  const [filter, setFilter] = useState<AuditFilter>(defaultFilter);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Audit Log</h1>
      <AuditFilters onFilter={setFilter} />
      <AuditGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
