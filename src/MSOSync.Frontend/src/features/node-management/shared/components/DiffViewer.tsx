import { useState } from 'react';
import { cn } from '../../../../lib/utils';
import type { RegistrationDiffItemDto, RegistrationChangeType } from '../../types/registration';

interface DiffViewerProps {
  items:       RegistrationDiffItemDto[];
  defaultView?: 'changes' | 'all';
}

function changeClass(changeType: RegistrationChangeType): string {
  switch (changeType) {
    case 'Modified': return 'bg-amber-50  dark:bg-amber-950/30';
    case 'Added':    return 'bg-green-50  dark:bg-green-950/30';
    case 'Removed':  return 'bg-red-50    dark:bg-red-950/30';
    default:         return 'bg-neutral-50 dark:bg-neutral-900';
  }
}

function ChangeBadge({ changeType }: { changeType: RegistrationChangeType }) {
  const classes: Record<RegistrationChangeType, string> = {
    Modified:  'bg-amber-100  text-amber-800  dark:bg-amber-900/50  dark:text-amber-300',
    Added:     'bg-green-100  text-green-800  dark:bg-green-900/50  dark:text-green-300',
    Removed:   'bg-red-100    text-red-800    dark:bg-red-900/50    dark:text-red-300',
    Unchanged: 'bg-neutral-100 text-neutral-600 dark:bg-neutral-800 dark:text-neutral-400',
  };
  return (
    <span className={cn('rounded px-1.5 py-0.5 text-xs font-medium', classes[changeType])}>
      {changeType}
    </span>
  );
}

export function DiffViewer({ items, defaultView = 'changes' }: DiffViewerProps) {
  const [view, setView] = useState<'changes' | 'all'>(defaultView);

  const visible = view === 'all'
    ? items
    : items.filter(i => i.changeType !== 'Unchanged');

  return (
    <div>
      <div className="flex items-center justify-between mb-2">
        <p className="text-xs text-neutral-500">
          {visible.length} field{visible.length !== 1 ? 's' : ''}
        </p>
        <button
          onClick={() => setView(v => v === 'changes' ? 'all' : 'changes')}
          className="text-xs text-blue-600 dark:text-blue-400 hover:underline"
        >
          {view === 'changes' ? 'Show All' : 'Only Changed'}
        </button>
      </div>
      <div className="rounded-md border overflow-hidden text-sm">
        <table className="w-full table-fixed">
          <thead>
            <tr className="bg-neutral-100 dark:bg-neutral-800 text-neutral-600 dark:text-neutral-400">
              <th className="text-left px-3 py-2 w-1/4">Field</th>
              <th className="text-left px-3 py-2 w-1/4">Current</th>
              <th className="text-left px-3 py-2 w-1/4">Incoming</th>
              <th className="text-left px-3 py-2 w-1/4">Change</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((item, i) => (
              <tr key={i} className={cn('border-t', changeClass(item.changeType))}>
                <td className="px-3 py-2 font-medium text-neutral-700 dark:text-neutral-300">
                  {item.field}
                </td>
                <td className="px-3 py-2 text-neutral-600 dark:text-neutral-400">
                  {item.currentValue ?? <span className="italic text-neutral-400">—</span>}
                </td>
                <td className="px-3 py-2 text-neutral-800 dark:text-neutral-200">
                  {item.incomingValue ?? <span className="italic text-neutral-400">—</span>}
                </td>
                <td className="px-3 py-2">
                  <ChangeBadge changeType={item.changeType} />
                </td>
              </tr>
            ))}
            {visible.length === 0 && (
              <tr>
                <td colSpan={4} className="px-3 py-4 text-center text-neutral-400 text-xs">
                  No {view === 'changes' ? 'changes' : 'fields'} to display.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
