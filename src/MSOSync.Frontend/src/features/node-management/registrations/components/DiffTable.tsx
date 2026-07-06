import { DiffViewer } from '../../shared/components/DiffViewer';
import type { RegistrationDiffDto } from '../../types/registration';

interface DiffTableProps {
  diff: RegistrationDiffDto;
}

export function DiffTable({ diff }: DiffTableProps) {
  return (
    <div className="mt-4">
      <h4 className="text-sm font-medium mb-2 text-neutral-700 dark:text-neutral-300">
        Field Diff
      </h4>
      <DiffViewer items={diff.items} defaultView="changes" />
    </div>
  );
}
