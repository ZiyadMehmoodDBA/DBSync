import { useState } from 'react';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '../../components/ui/select';
import { Badge } from '../../components/ui/badge';
import { ConfirmDialog } from '../../shared/components/actions';
import { formatRelativeTime } from '../../shared/utils/date';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import { useTemplates } from './hooks';
import { useArchiveTemplate, usePublishTemplate } from './mutations';
import type { TemplateSummaryDto } from './types';

export function TemplatesPage() {
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [search, setSearch] = useState('');
  const [archiveTarget, setArchiveTarget] = useState<TemplateSummaryDto | null>(null);

  const { data: templates = [], isLoading } = useTemplates(statusFilter || undefined);
  const archiveMutation = useArchiveTemplate();
  const publishMutation = usePublishTemplate();

  const filtered = templates.filter((t) =>
    !search || t.name.toLowerCase().includes(search.toLowerCase()),
  );

  const handleArchive = async () => {
    if (!archiveTarget) return;
    try {
      await archiveMutation.mutateAsync(archiveTarget.id);
      toast.success(`Template "${archiveTarget.name}" archived`);
    } catch (e) {
      toast.error(getErrorMessage(e));
    } finally {
      setArchiveTarget(null);
    }
  };

  const handlePublish = async (t: TemplateSummaryDto) => {
    try {
      await publishMutation.mutateAsync(t.id);
      toast.success(`Template "${t.name}" published`);
    } catch (e) {
      toast.error(getErrorMessage(e));
    }
  };

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Configuration Templates</h1>
      </div>

      <div className="flex items-center gap-3">
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search templates…"
          className="max-w-xs"
        />
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="w-36">
            <SelectValue placeholder="All statuses" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="">All</SelectItem>
            <SelectItem value="Draft">Draft</SelectItem>
            <SelectItem value="Published">Published</SelectItem>
            <SelectItem value="Archived">Archived</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {isLoading ? (
        <p className="text-sm text-neutral-500">Loading templates…</p>
      ) : filtered.length === 0 ? (
        <p className="text-sm text-neutral-500">No templates found.</p>
      ) : (
        <div className="divide-y rounded-lg border">
          {filtered.map((t) => (
            <div key={t.id} className="flex items-center justify-between px-4 py-3">
              <div className="flex flex-col gap-0.5">
                <div className="flex items-center gap-2">
                  <span className="font-medium text-sm">{t.name}</span>
                  <Badge variant={t.status === 'Published' ? 'default' : 'secondary'}>
                    {t.status}
                  </Badge>
                </div>
                <span className="text-xs text-neutral-500">
                  {t.currentPublishedVersion != null
                    ? `Published v${t.currentPublishedVersion}`
                    : t.latestDraftVersion != null
                    ? `Draft v${t.latestDraftVersion}`
                    : 'No versions'}
                  {' · '}Updated {formatRelativeTime(t.updatedAt)}
                </span>
              </div>
              <div className="flex items-center gap-2">
                {t.status === 'Draft' && (
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={publishMutation.isPending}
                    onClick={() => void handlePublish(t)}
                  >
                    Publish
                  </Button>
                )}
                {t.status !== 'Archived' && (
                  <Button
                    size="sm"
                    variant="ghost"
                    onClick={() => setArchiveTarget(t)}
                  >
                    Archive
                  </Button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {archiveTarget && (
        <ConfirmDialog
          open
          title="Archive Template"
          description={`Archive template "${archiveTarget.name}"? Nodes assigned to this template will not be affected.`}
          confirmLabel="Archive"
          variant="destructive"
          loading={archiveMutation.isPending}
          onConfirm={() => void handleArchive()}
          onOpenChange={(open) => { if (!open) setArchiveTarget(null); }}
        />
      )}
    </div>
  );
}
