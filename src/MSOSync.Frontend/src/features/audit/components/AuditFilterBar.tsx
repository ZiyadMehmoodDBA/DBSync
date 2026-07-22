import { useState } from 'react';
import { X, Bookmark } from 'lucide-react';
import { Button } from '@/components/ui/button';

export interface AuditFilterState {
  usernames:   string[];
  actionNames: string[];
  objectNames: string[];
  from:        string;
  to:          string;
}

const EMPTY_FILTER: AuditFilterState = {
  usernames: [], actionNames: [], objectNames: [], from: '', to: '',
};

interface Props {
  value:         AuditFilterState;
  onChange:      (f: AuditFilterState) => void;
  onSave:        (name: string) => void;
  knownActions?: string[];
}

function Chip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary">
      {label}
      <button onClick={onRemove} className="hover:text-destructive"><X className="h-3 w-3" /></button>
    </span>
  );
}

function MultiSelectChips({
  label,
  values,
  options,
  onAdd,
  onRemove,
}: {
  label:    string;
  values:   string[];
  options?: string[];
  onAdd:    (v: string) => void;
  onRemove: (v: string) => void;
}) {
  const [input, setInput] = useState('');
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <div className="flex flex-wrap items-center gap-1 rounded border bg-background px-2 py-1 min-h-[32px]">
        {values.map(v => <Chip key={v} label={v} onRemove={() => onRemove(v)} />)}
        {options ? (
          <select
            className="text-xs bg-transparent outline-none cursor-pointer"
            value=""
            onChange={e => { if (e.target.value) onAdd(e.target.value); }}
          >
            <option value="">+ Add…</option>
            {options.filter(o => !values.includes(o)).map(o => (
              <option key={o} value={o}>{o}</option>
            ))}
          </select>
        ) : (
          <input
            className="flex-1 min-w-[80px] text-xs bg-transparent outline-none"
            placeholder="Type + Enter"
            value={input}
            onChange={e => setInput(e.target.value)}
            onKeyDown={e => {
              if (e.key === 'Enter' && input.trim()) {
                onAdd(input.trim());
                setInput('');
              }
            }}
          />
        )}
      </div>
    </div>
  );
}

export function AuditFilterBar({ value, onChange, onSave, knownActions }: Props) {
  const [saveDialogOpen, setSaveDialogOpen] = useState(false);
  const [saveName, setSaveName]             = useState('');

  const update = (patch: Partial<AuditFilterState>) => onChange({ ...value, ...patch });

  const addItem    = (key: keyof Pick<AuditFilterState, 'usernames' | 'actionNames' | 'objectNames'>,
                      v: string) =>
    update({ [key]: [...value[key], v].filter((x, i, a) => a.indexOf(x) === i) });

  const removeItem = (key: keyof Pick<AuditFilterState, 'usernames' | 'actionNames' | 'objectNames'>,
                      v: string) =>
    update({ [key]: value[key].filter(x => x !== v) });

  const anyActive = value.usernames.length > 0 || value.actionNames.length > 0
    || value.objectNames.length > 0 || value.from || value.to;

  return (
    <div className="space-y-3 rounded-lg border bg-card p-3">
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <MultiSelectChips
          label="Usernames"
          values={value.usernames}
          onAdd={v => addItem('usernames', v)}
          onRemove={v => removeItem('usernames', v)}
        />
        <MultiSelectChips
          label="Actions"
          values={value.actionNames}
          options={knownActions}
          onAdd={v => addItem('actionNames', v)}
          onRemove={v => removeItem('actionNames', v)}
        />
        <MultiSelectChips
          label="Object Names"
          values={value.objectNames}
          onAdd={v => addItem('objectNames', v)}
          onRemove={v => removeItem('objectNames', v)}
        />
      </div>

      <div className="flex items-center gap-3">
        <div className="flex items-center gap-2">
          <label className="text-xs text-muted-foreground">From</label>
          <input
            type="datetime-local"
            className="rounded border bg-background px-2 py-1 text-xs"
            value={value.from}
            onChange={e => update({ from: e.target.value })}
          />
        </div>
        <div className="flex items-center gap-2">
          <label className="text-xs text-muted-foreground">To</label>
          <input
            type="datetime-local"
            className="rounded border bg-background px-2 py-1 text-xs"
            value={value.to}
            onChange={e => update({ to: e.target.value })}
          />
        </div>

        <div className="ml-auto flex items-center gap-2">
          {anyActive && (
            <>
              <Button variant="ghost" size="sm" onClick={() => onChange(EMPTY_FILTER)}>
                Clear All
              </Button>
              {saveDialogOpen ? (
                <div className="flex items-center gap-1">
                  <input
                    autoFocus
                    placeholder="Filter name…"
                    className="rounded border px-2 py-1 text-xs w-32"
                    value={saveName}
                    onChange={e => setSaveName(e.target.value)}
                    onKeyDown={e => {
                      if (e.key === 'Enter' && saveName.trim()) {
                        onSave(saveName.trim());
                        setSaveName('');
                        setSaveDialogOpen(false);
                      }
                      if (e.key === 'Escape') setSaveDialogOpen(false);
                    }}
                  />
                  <Button size="sm" onClick={() => {
                    if (saveName.trim()) {
                      onSave(saveName.trim());
                      setSaveName('');
                      setSaveDialogOpen(false);
                    }
                  }}>Save</Button>
                </div>
              ) : (
                <Button variant="outline" size="sm" onClick={() => setSaveDialogOpen(true)}>
                  <Bookmark className="h-3 w-3 mr-1" /> Save Filter
                </Button>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
