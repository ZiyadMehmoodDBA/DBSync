import { BulkActionToolbar } from './BulkActionToolbar';
import { RegistrationQueue } from './RegistrationQueue';
import { RegistrationDetailPanel } from './RegistrationDetailPanel';

export function RegistrationsTab() {
  return (
    <div className="flex flex-col h-full">
      <BulkActionToolbar />
      <div className="flex flex-1 overflow-hidden">
        <div className="w-72 shrink-0 border-r dark:border-neutral-800 overflow-y-auto">
          <RegistrationQueue />
        </div>
        <div className="flex-1 overflow-y-auto">
          <RegistrationDetailPanel />
        </div>
      </div>
    </div>
  );
}
