import client from './client';

export async function getPreferences(): Promise<Record<string, unknown>> {
  return client.get<Record<string, unknown>>('/preferences').then(r => r.data);
}

export async function upsertPreference(key: string, value: unknown): Promise<void> {
  await client.put(`/preferences/${encodeURIComponent(key)}`, value);
}

export async function bulkUpsertPreferences(
  prefs: Record<string, unknown>,
): Promise<void> {
  await client.put('/preferences', prefs);
}

export async function deletePreference(key: string): Promise<void> {
  await client.delete(`/preferences/${encodeURIComponent(key)}`);
}
