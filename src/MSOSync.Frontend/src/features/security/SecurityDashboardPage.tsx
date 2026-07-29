import { useState } from 'react';
import { ShieldCheck, ShieldAlert } from 'lucide-react';
import { useAuditLog, useChainVerify } from './hooks';

export function SecurityDashboardPage() {
  const [page, setPage] = useState(1);
  const { data: auditData, isLoading: auditLoading, error: auditError } = useAuditLog(page);
  const { data: chainData, isLoading: chainLoading } = useChainVerify();

  const totalPages = auditData ? Math.ceil(auditData.total / 50) : 0;

  return (
    <div className="space-y-6 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Security Dashboard</h1>
      </div>

      {/* Chain integrity status card */}
      <div className="rounded-lg border border-neutral-200 dark:border-neutral-800 p-4">
        <h2 className="text-lg font-medium mb-3">Audit Chain Integrity</h2>
        {chainLoading ? (
          <div className="text-muted-foreground text-sm">Verifying chain integrity…</div>
        ) : chainData ? (
          chainData.is_valid ? (
            <div className="flex items-center gap-2 text-green-600 dark:text-green-400">
              <ShieldCheck className="h-5 w-5 shrink-0" />
              <span className="font-medium">Chain valid — all audit entries are intact</span>
            </div>
          ) : (
            <div className="flex items-center gap-2 text-red-600 dark:text-red-400">
              <ShieldAlert className="h-5 w-5 shrink-0" />
              <span className="font-medium">
                Chain integrity broken
                {chainData.first_broken_id != null
                  ? ` — first broken entry: #${chainData.first_broken_id}`
                  : ''}
              </span>
            </div>
          )
        ) : (
          <div className="text-muted-foreground text-sm">Unable to verify chain integrity</div>
        )}
      </div>

      {/* Audit log table */}
      <div className="rounded-lg border border-neutral-200 dark:border-neutral-800">
        <div className="flex items-center justify-between p-4 border-b border-neutral-200 dark:border-neutral-800">
          <h2 className="text-lg font-medium">Audit Log</h2>
          {auditData && (
            <span className="text-sm text-muted-foreground">
              {auditData.total} total entries
            </span>
          )}
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-neutral-200 dark:border-neutral-800 bg-neutral-50 dark:bg-neutral-900/50">
                <th className="p-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Audit ID</th>
                <th className="p-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Action</th>
                <th className="p-3 text-left font-medium text-neutral-600 dark:text-neutral-400">User</th>
                <th className="p-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Object</th>
                <th className="p-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Time</th>
              </tr>
            </thead>
            <tbody>
              {auditLoading ? (
                <tr>
                  <td colSpan={5} className="p-6 text-center text-muted-foreground">
                    Loading audit log…
                  </td>
                </tr>
              ) : auditError ? (
                <tr>
                  <td colSpan={5} className="p-6 text-center text-red-500">
                    Failed to load audit log
                  </td>
                </tr>
              ) : !auditData || auditData.items.length === 0 ? (
                <tr>
                  <td colSpan={5} className="p-6 text-center text-muted-foreground">
                    No audit entries found
                  </td>
                </tr>
              ) : (
                auditData.items.map((entry) => (
                  <tr
                    key={entry.auditId}
                    className="border-b border-neutral-100 dark:border-neutral-800/60 hover:bg-neutral-50 dark:hover:bg-neutral-800/30"
                  >
                    <td className="p-3 font-mono text-xs text-neutral-500 dark:text-neutral-400">
                      {entry.auditId}
                    </td>
                    <td className="p-3">{entry.actionName ?? '—'}</td>
                    <td className="p-3 text-neutral-600 dark:text-neutral-400">
                      {entry.username ?? '—'}
                    </td>
                    <td className="p-3 text-neutral-600 dark:text-neutral-400">
                      {entry.objectName ?? '—'}
                    </td>
                    <td className="p-3 text-muted-foreground text-xs">
                      {entry.createTime
                        ? new Date(entry.createTime).toLocaleString()
                        : '—'}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        {auditData && auditData.total > 50 && (
          <div className="flex items-center justify-center gap-2 p-4 border-t border-neutral-200 dark:border-neutral-800">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1}
              className="px-3 py-1 border border-neutral-200 dark:border-neutral-700 rounded text-sm disabled:opacity-40 hover:bg-neutral-50 dark:hover:bg-neutral-800 transition-colors"
            >
              Previous
            </button>
            <span className="px-3 py-1 text-sm text-muted-foreground">
              Page {page} of {totalPages}
            </span>
            <button
              onClick={() => setPage((p) => p + 1)}
              disabled={page >= totalPages}
              className="px-3 py-1 border border-neutral-200 dark:border-neutral-700 rounded text-sm disabled:opacity-40 hover:bg-neutral-50 dark:hover:bg-neutral-800 transition-colors"
            >
              Next
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
