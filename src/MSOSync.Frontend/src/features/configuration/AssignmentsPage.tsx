import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { ConfirmDialog } from '../../shared/components/actions';
import { ConfigurationStateBadge } from '../../components/ui/ConfigurationStateBadge';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import { useDriftNodes } from './hooks';
import { useAssignTemplate, useUnassignTemplate } from './mutations';
import { useTemplates } from './hooks';
import { TemplateVersionSelect } from '../../components/ui/TemplateVersionSelect';
import type { DriftNodeDto } from './types';

export function AssignmentsPage() {
  const [search, setSearch] = useState('');
  const [unassignTarget, setUnassignTarget] = useState<DriftNodeDto | null>(null);
  const [assignTarget, setAssignTarget] = useState<DriftNodeDto | null>(null);
  const [assignForm, setAssignForm] = useState<{ templateId: string; version: number | null }>({
    templateId: '', version: null,
  });

  const { data: nodes = [], isLoading } = useDriftNodes({ search: search || undefined });
  const { data: templates = [] } = useTemplates('Published');

  const unassignMutation = useUnassignTemplate();
  const assignMutation = useAssignTemplate();

  const handleUnassign = async () => {
    if (!unassignTarget) return;
    try {
      await unassignMutation.mutateAsync(unassignTarget.nodeId);
      toast.success(`Template unassigned from ${unassignTarget.nodeName}`);
    } catch (e) {
      toast.error(getErrorMessage(e));
    } finally {
      setUnassignTarget(null);
    }
  };

  const handleAssign = async () => {
    if (!assignTarget || !assignForm.templateId || !assignForm.version) return;
    try {
      await assignMutation.mutateAsync({
        nodeId: assignTarget.nodeId,
        data: { templateId: assignForm.templateId, version: assignForm.version },
      });
      toast.success(`Template assigned to ${assignTarget.nodeName}`);
    } catch (e) {
      toast.error(getErrorMessage(e));
    } finally {
      setAssignTarget(null);
      setAssignForm({ templateId: '', version: null });
    }
  };

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Node Assignments</h1>
      </div>

      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search nodes…"
        className="max-w-xs"
      />

      {isLoading ? (
        <p className="text-sm text-neutral-500">Loading…</p>
      ) : nodes.length === 0 ? (
        <p className="text-sm text-neutral-500">No nodes found.</p>
      ) : (
        <div className="divide-y rounded-lg border">
          {nodes.map((n) => (
            <div key={n.nodeId} className="flex items-center justify-between px-4 py-3">
              <div className="flex flex-col gap-0.5">
                <div className="flex items-center gap-2">
                  <span className="font-medium text-sm">{n.nodeName}</span>
                  <ConfigurationStateBadge state={n.configurationState} />
                </div>
                <span className="text-xs text-neutral-500">
                  {n.assignedTemplateName
                    ? `${n.assignedTemplateName} v${n.assignedTemplateVersion}`
                    : 'No template assigned'}
                  {n.appliedTemplateVersion != null && ` · Applied v${n.appliedTemplateVersion}`}
                </span>
              </div>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => setAssignTarget(n)}
                >
                  Assign
                </Button>
                {n.assignedTemplateId && (
                  <Button
                    size="sm"
                    variant="ghost"
                    onClick={() => setUnassignTarget(n)}
                  >
                    Unassign
                  </Button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Assign dialog */}
      {assignTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-96 rounded-lg bg-white dark:bg-neutral-900 p-6 shadow-xl flex flex-col gap-4">
            <h2 className="font-semibold">Assign Template to {assignTarget.nodeName}</h2>
            <div className="flex flex-col gap-2">
              <label className="text-sm text-neutral-600">Template</label>
              <select
                className="rounded border px-3 py-2 text-sm"
                value={assignForm.templateId}
                onChange={(e) => setAssignForm({ templateId: e.target.value, version: null })}
              >
                <option value="">Select template…</option>
                {templates.map((t) => (
                  <option key={t.id} value={t.id}>{t.name}</option>
                ))}
              </select>
            </div>
            {assignForm.templateId && (
              <div className="flex flex-col gap-2">
                <label className="text-sm text-neutral-600">Version</label>
                <TemplateVersionSelect
                  templateId={assignForm.templateId}
                  value={assignForm.version}
                  onChange={(v) => setAssignForm((f) => ({ ...f, version: v }))}
                />
              </div>
            )}
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setAssignTarget(null)}>Cancel</Button>
              <Button
                disabled={!assignForm.templateId || !assignForm.version || assignMutation.isPending}
                onClick={() => void handleAssign()}
              >
                Assign
              </Button>
            </div>
          </div>
        </div>
      )}

      {unassignTarget && (
        <ConfirmDialog
          open
          title="Unassign Template"
          description={`Remove template assignment from "${unassignTarget.nodeName}"?`}
          confirmLabel="Unassign"
          variant="destructive"
          loading={unassignMutation.isPending}
          onConfirm={() => void handleUnassign()}
          onOpenChange={(open) => { if (!open) setUnassignTarget(null); }}
        />
      )}
    </div>
  );
}
