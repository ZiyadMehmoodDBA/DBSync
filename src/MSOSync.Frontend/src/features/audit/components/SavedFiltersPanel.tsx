import { Trash2 } from 'lucide-react';
import type { AuditFilterState } from './AuditFilterBar';

interface SavedFilter {
  name:   string;
  filter: AuditFilterState;
}

interface Props {
  filters:  SavedFilter[];
  onLoad:   (f: AuditFilterState) => void;
  onDelete: (name: string) => void;
}

export function SavedFiltersPanel({ filters, onLoad, onDelete }: Props) {
  if (filters.length === 0) {
    return <p className="text-xs text-muted-foreground p-2">No saved filters.</p>;
  }
  return (
    <div className="space-y-1">
      {filters.map(sf => (
        <div key={sf.name} className="flex items-center justify-between rounded px-2 py-1.5 hover:bg-muted/50 group">
          <button
            className="text-sm text-left flex-1 truncate"
            onClick={() => onLoad(sf.filter)}
          >
            {sf.name}
          </button>
          <button
            className="opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-destructive"
            onClick={() => onDelete(sf.name)}
          >
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        </div>
      ))}
    </div>
  );
}
