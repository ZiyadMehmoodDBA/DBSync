import { useState } from 'react';
import { useConfigComparison } from '@/shared/hooks/useConfigComparison';
import { Button } from '@/components/ui/button';
import { X } from 'lucide-react';
import type { ChangeType } from '@/shared/types/configComparison';

interface Props {
  templateId: string;
  availableVersions: { versionNumber: number; label: string }[];
  onClose: () => void;
}

const ROW_COLOR: Record<ChangeType, string> = {
  Changed:   'bg-yellow-50 dark:bg-yellow-950/20',
  Added:     'bg-green-50  dark:bg-green-950/20',
  Removed:   'bg-red-50    dark:bg-red-950/20',
  Unchanged: '',
};

const BADGE_COLOR: Record<ChangeType, string> = {
  Changed:   'text-yellow-700 bg-yellow-100',
  Added:     'text-green-700  bg-green-100',
  Removed:   'text-red-700    bg-red-100',
  Unchanged: 'text-gray-500   bg-gray-100',
};

export function ConfigComparePanel({ templateId, availableVersions, onClose }: Props) {
  const [v1, setV1] = useState<number | null>(null);
  const [v2, setV2] = useState<number | null>(null);
  const [showUnchanged, setShowUnchanged] = useState(false);

  const { data, isFetching, error } = useConfigComparison(templateId, v1, v2);

  const unchangedCount = data?.entries.filter(e => e.changeType === 'Unchanged').length ?? 0;
  const visibleEntries = showUnchanged
    ? data?.entries
    : data?.entries.filter(e => e.changeType !== 'Unchanged');

  return (
    <div className="fixed inset-y-0 right-0 z-50 w-[680px] border-l bg-background shadow-xl flex flex-col">
      {/* Header */}
      <div className="flex items-center justify-between border-b px-4 py-3">
        <h2 className="font-semibold text-sm">Compare Template Versions</h2>
        <Button variant="ghost" size="icon" onClick={onClose} aria-label="Close">
          <X className="h-4 w-4" />
        </Button>
      </div>

      {/* Version pickers */}
      <div className="flex items-center gap-3 border-b px-4 py-3">
        <div className="flex-1 space-y-1">
          <label className="text-xs text-muted-foreground">From version (V1)</label>
          <select
            className="w-full rounded border bg-background px-2 py-1.5 text-sm"
            value={v1 ?? ''}
            onChange={e => setV1(e.target.value ? Number(e.target.value) : null)}
          >
            <option value="">Select…</option>
            {availableVersions.map(v => (
              <option key={v.versionNumber} value={v.versionNumber}>{v.label}</option>
            ))}
          </select>
        </div>
        <span className="mt-5 text-muted-foreground">→</span>
        <div className="flex-1 space-y-1">
          <label className="text-xs text-muted-foreground">To version (V2)</label>
          <select
            className="w-full rounded border bg-background px-2 py-1.5 text-sm"
            value={v2 ?? ''}
            onChange={e => setV2(e.target.value ? Number(e.target.value) : null)}
          >
            <option value="">Select…</option>
            {availableVersions.map(v => (
              <option key={v.versionNumber} value={v.versionNumber}>{v.label}</option>
            ))}
          </select>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto">
        {!v1 || !v2 ? (
          <div className="p-6 text-sm text-muted-foreground">Select two different versions to compare.</div>
        ) : v1 === v2 ? (
          <div className="p-6 text-sm text-muted-foreground">V1 and V2 must be different versions.</div>
        ) : isFetching ? (
          <div className="p-6 text-sm text-muted-foreground">Loading diff…</div>
        ) : error ? (
          <div className="p-6 text-sm text-destructive">Failed to load diff.</div>
        ) : !data ? null : data.entries.length === 0 ? (
          <div className="p-6 text-sm text-muted-foreground">No differences found.</div>
        ) : (
          <>
            {/* Summary */}
            <div className="flex items-center gap-3 px-4 py-2 border-b text-xs text-muted-foreground bg-muted/30">
              <span>{data.v1Label}</span>
              <span>→</span>
              <span>{data.v2Label}</span>
              {!data.hasChanges && <span className="ml-auto text-green-600">No differences</span>}
            </div>

            {/* Diff table */}
            <table className="w-full text-xs">
              <thead className="sticky top-0 bg-muted/80 border-b">
                <tr>
                  <th className="px-3 py-2 text-left font-medium w-1/3">Key</th>
                  <th className="px-3 py-2 text-left font-medium w-16">Change</th>
                  <th className="px-3 py-2 text-left font-medium">Old Value</th>
                  <th className="px-3 py-2 text-left font-medium">New Value</th>
                </tr>
              </thead>
              <tbody>
                {visibleEntries?.map((entry, i) => (
                  <tr key={i} className={`border-b ${ROW_COLOR[entry.changeType]}`}>
                    <td className="px-3 py-2 font-mono font-medium">{entry.key}</td>
                    <td className="px-3 py-2">
                      <span className={`rounded px-1.5 py-0.5 text-[10px] font-semibold ${BADGE_COLOR[entry.changeType]}`}>
                        {entry.changeType}
                      </span>
                    </td>
                    <td className="px-3 py-2 font-mono text-muted-foreground">
                      {entry.oldValue ?? '—'}
                    </td>
                    <td className="px-3 py-2 font-mono">
                      {entry.newValue ?? '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {/* Show unchanged toggle */}
            {unchangedCount > 0 && (
              <div className="px-4 py-2 border-t">
                <button
                  className="text-xs text-muted-foreground hover:text-foreground underline"
                  onClick={() => setShowUnchanged(prev => !prev)}
                >
                  {showUnchanged ? `Hide ${unchangedCount} unchanged` : `Show ${unchangedCount} unchanged`}
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
