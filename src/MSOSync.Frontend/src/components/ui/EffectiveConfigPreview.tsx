import type { ConfigurationSettings, NodeOverrideDto } from '../../features/configuration/types';

interface Props {
  settings: ConfigurationSettings;
  overrides?: NodeOverrideDto[];
}

const FIELD_LABELS: Partial<Record<keyof ConfigurationSettings, string>> = {
  heartbeatIntervalSeconds: 'Heartbeat Interval (s)',
  transportMode:            'Transport Mode',
  maxRetryAttempts:         'Max Retry Attempts',
  retryBackoffSeconds:      'Retry Backoff (s)',
  batchSizeLimit:           'Batch Size Limit',
  minimumAgentVersion:      'Min Agent Version',
};

export function EffectiveConfigPreview({ settings, overrides = [] }: Props) {
  const overrideKeys = new Set(overrides.map((o) => o.settingKey));

  const fields = (Object.keys(FIELD_LABELS) as (keyof ConfigurationSettings)[]).map((key) => ({
    key,
    label: FIELD_LABELS[key]!,
    value: settings[key],
    isOverridden: overrideKeys.has(key),
  }));

  return (
    <div className="divide-y text-sm">
      {fields.map((f) => (
        <div key={f.key} className="flex items-center justify-between py-1.5">
          <span className="text-gray-600">{f.label}</span>
          <div className="flex items-center gap-2">
            <span className="font-mono text-gray-900">{JSON.stringify(f.value)}</span>
            {f.isOverridden && (
              <span className="text-xs text-orange-600 bg-orange-50 rounded px-1.5 py-0.5 border border-orange-200">
                Override
              </span>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
