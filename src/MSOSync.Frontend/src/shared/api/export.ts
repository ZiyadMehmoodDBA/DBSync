import client from './client';

export type ExportResource =
  | 'events'
  | 'incoming-batches'
  | 'outgoing-batches'
  | 'audit'
  | 'nodes'
  | 'channels'
  | 'triggers'
  | 'routers'
  | 'users'
  | 'parameters';

export type ExportFormat = 'csv' | 'json';

export async function downloadExport(
  resource: ExportResource,
  format: ExportFormat,
  params: Record<string, string | number | boolean | undefined>,
): Promise<void> {
  const response = await client.get<Blob>(`/${resource}/export`, {
    params: { format, ...params },
    responseType: 'blob',
  });
  const date = new Date().toISOString().split('T')[0];
  triggerDownload(response.data, `${resource}-${date}.${format}`);
}

export function downloadCurrentViewCsv(
  data: Record<string, unknown>[],
  resource: ExportResource,
): void {
  if (data.length === 0) return;
  const headers = Object.keys(data[0]);
  const rows = data.map((row) =>
    headers.map((h) => csvEscape(String(row[h] ?? ''))).join(','),
  );
  const csv = [headers.join(','), ...rows].join('\n');
  triggerDownload(new Blob([csv], { type: 'text/csv' }), `${resource}-view.csv`);
}

export function downloadCurrentViewJson(
  data: Record<string, unknown>[],
  resource: ExportResource,
): void {
  triggerDownload(
    new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }),
    `${resource}-view.json`,
  );
}

function triggerDownload(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

function csvEscape(s: string): string {
  if (s.includes(',') || s.includes('"') || s.includes('\n') || s.includes('\r'))
    return `"${s.replace(/"/g, '""')}"`;
  return s;
}
