import { useState } from 'react';
import type { AuditFilter } from '../../shared/types';
import { AuditFilters } from './AuditFilters';
import { AuditGrid } from './AuditGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';
import { useAuditLog } from './hooks';

const defaultFilter: AuditFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE };

export function AuditPage() {
  const [filter, setFilter] = useState<AuditFilter>(defaultFilter);
  const { data } = useAuditLog(filter);

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Audit Log</h1>
        <ExportMenu
          resource="audit"
          currentData={(data?.data ?? []) as unknown as Record<string, unknown>[]}
          queryParams={filter as unknown as Record<string, string | number | boolean | undefined>}
        />
      </div>
      <AuditFilters onFilter={setFilter} />
      <AuditGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
