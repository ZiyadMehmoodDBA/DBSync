import { useState } from 'react';
import { Download, Loader2, RefreshCw, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '../../components/ui/button';
import { Badge } from '../../components/ui/badge';
import { Progress } from '../../components/ui/progress';
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from '../../components/ui/table';
import { useExportJobs, useDeleteExportJobMutation, useCreateExportJobMutation } from '../../shared/hooks/useExportJobs';
import { getClientToken } from '../../shared/api/client';
import { ExportJobStatus, type ExportJobDto } from '../../shared/types/export';

async function downloadFile(jobId: string, filename: string, token: string): Promise<void> {
  const response = await fetch(`/api/v1/export-jobs/${jobId}/download`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok) throw new Error('Download failed');
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  switch (status) {
    case ExportJobStatus.Completed: return 'default';
    case ExportJobStatus.Running:   return 'secondary';
    case ExportJobStatus.Failed:    return 'destructive';
    default:                        return 'outline';
  }
}

function formatRelative(iso: string | null): string {
  if (!iso) return '—';
  const diff = Date.now() - new Date(iso).getTime();
  const mins = Math.floor(diff / 60_000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.floor(hrs / 24)}d ago`;
}

export function DownloadsPage() {
  const { data: jobs = [], isLoading } = useExportJobs();
  const { mutate: deleteJob } = useDeleteExportJobMutation();
  const [downloadingIds, setDownloadingIds] = useState<Set<string>>(new Set());

  function handleDelete(job: ExportJobDto) {
    deleteJob(job.jobId, {
      onSuccess: () => toast.success('Export deleted'),
      onError:   () => toast.error('Failed to delete export'),
    });
  }

  async function handleDownload(job: ExportJobDto) {
    const token = getClientToken() ?? '';
    const filename = `${job.resourceType}-export-${job.jobId}.${job.format}`;
    setDownloadingIds(prev => new Set(prev).add(job.jobId));
    try {
      await downloadFile(job.jobId, filename, token);
    } catch {
      toast.error('Download failed');
    } finally {
      setDownloadingIds(prev => {
        const next = new Set(prev);
        next.delete(job.jobId);
        return next;
      });
    }
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Downloads</h1>
        <span className="text-sm text-muted-foreground">{jobs.length} export{jobs.length !== 1 ? 's' : ''}</span>
      </div>

      {jobs.length === 0 ? (
        <div className="flex flex-col items-center gap-2 p-12 text-muted-foreground">
          <Download className="h-8 w-8" />
          <p>No exports yet. Use the Export menu on any page to queue a download.</p>
        </div>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Resource</TableHead>
              <TableHead>Format</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Progress</TableHead>
              <TableHead className="text-right">Rows</TableHead>
              <TableHead>Created</TableHead>
              <TableHead>Completed</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {jobs.map((job) => (
              <TableRow key={job.jobId}>
                <TableCell className="capitalize">{job.resourceType.replace(/-/g, ' ')}</TableCell>
                <TableCell className="uppercase text-xs">{job.format}</TableCell>
                <TableCell>
                  <Badge variant={statusVariant(job.status)}>{job.status}</Badge>
                </TableCell>
                <TableCell className="w-32">
                  {job.status === ExportJobStatus.Running ? (
                    <Progress value={job.progressPercent} className="h-2" />
                  ) : job.status === ExportJobStatus.Completed ? (
                    <Progress value={100} className="h-2" />
                  ) : (
                    <span className="text-muted-foreground text-xs">—</span>
                  )}
                </TableCell>
                <TableCell className="text-right">
                  {job.rowCount?.toLocaleString() ?? '—'}
                </TableCell>
                <TableCell className="text-sm text-muted-foreground">
                  {formatRelative(job.createdAt)}
                </TableCell>
                <TableCell className="text-sm text-muted-foreground">
                  {formatRelative(job.completedAt)}
                </TableCell>
                <TableCell>
                  <div className="flex items-center gap-1 justify-end">
                    {job.status === ExportJobStatus.Completed && (
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={downloadingIds.has(job.jobId)}
                        onClick={() => handleDownload(job)}
                      >
                        {downloadingIds.has(job.jobId)
                          ? <Loader2 className="h-4 w-4 animate-spin" />
                          : <Download className="h-4 w-4" />}
                      </Button>
                    )}
                    {job.status === ExportJobStatus.Failed && (
                      <RetryButton job={job} />
                    )}
                    {(job.status === ExportJobStatus.Completed
                      || job.status === ExportJobStatus.Failed) && (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => handleDelete(job)}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}

function RetryButton({ job }: { job: ExportJobDto }) {
  const { mutate: createJob, isPending } = useCreateExportJobMutation();

  return (
    <Button
      variant="outline"
      size="sm"
      disabled={isPending}
      onClick={() =>
        createJob(
          {
            resourceType: job.resourceType,
            format:       job.format,
            filtersJson:  job.filtersJson,
            parentJobId:  job.jobId,
          },
          { onSuccess: () => toast.success('Export re-queued') }
        )
      }
    >
      {isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
    </Button>
  );
}
