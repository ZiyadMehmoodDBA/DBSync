import type { ConfigurationSettings } from '../../features/configuration/types';

interface Props {
  settings1: ConfigurationSettings;
  settings2: ConfigurationSettings;
  label1?: string;
  label2?: string;
}

type DiffEntry =
  | { kind: 'modified'; key: string; v1: unknown; v2: unknown }
  | { kind: 'added'; key: string; v2: unknown }
  | { kind: 'removed'; key: string; v1: unknown };

function computeDiff(s1: ConfigurationSettings, s2: ConfigurationSettings): DiffEntry[] {
  const keys = new Set([...Object.keys(s1), ...Object.keys(s2)]);
  const entries: DiffEntry[] = [];

  for (const key of keys) {
    const v1 = (s1 as unknown as Record<string, unknown>)[key];
    const v2 = (s2 as unknown as Record<string, unknown>)[key];
    if (JSON.stringify(v1) === JSON.stringify(v2)) continue;

    if (key in s1 && key in s2) entries.push({ kind: 'modified', key, v1, v2 });
    else if (key in s2)         entries.push({ kind: 'added', key, v2 });
    else                        entries.push({ kind: 'removed', key, v1 });
  }

  return entries;
}

export function ConfigurationDiffViewer({
  settings1, settings2, label1 = 'Before', label2 = 'After',
}: Props) {
  const entries = computeDiff(settings1, settings2);

  if (entries.length === 0) {
    return <p className="text-sm text-gray-500">No differences between versions.</p>;
  }

  return (
    <div className="space-y-2 text-sm font-mono">
      <div className="flex gap-4 text-xs text-gray-500 mb-2">
        <span className="text-red-600">− {label1}</span>
        <span className="text-green-600">+ {label2}</span>
      </div>
      {entries.map((entry) => (
        <div key={entry.key} className="space-y-0.5">
          <div className="font-semibold text-gray-700 font-sans">{entry.key}</div>
          {(entry.kind === 'removed' || entry.kind === 'modified') && (
            <div className="pl-2 text-red-700 bg-red-50 rounded px-1">
              − {JSON.stringify('v1' in entry ? entry.v1 : undefined)}
            </div>
          )}
          {(entry.kind === 'added' || entry.kind === 'modified') && (
            <div className="pl-2 text-green-700 bg-green-50 rounded px-1">
              + {JSON.stringify('v2' in entry ? entry.v2 : undefined)}
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
