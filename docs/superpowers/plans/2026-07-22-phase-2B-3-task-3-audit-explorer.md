# Task 3 — Audit Explorer

**Files:**
- Modify: `src/MSOSync.Metadata/Audit/AuditFilter.cs`
- Modify: `src/MSOSync.Metadata/Audit/AuditFilterValidator.cs`
- Modify: `src/MSOSync.Metadata/Audit/IAuditQueryService.cs`
- Modify: `src/MSOSync.Metadata/Audit/AuditQueryService.cs`
- Modify: `src/MSOSync.Api/Controllers/AuditController.cs`
- Create: `tests/MSOSync.MetadataTests/Audit/AuditQueryServiceMultiFilterTests.cs`
- Create: `src/MSOSync.Frontend/src/features/operations/activity/components/AuditFilterBar.tsx`
- Create: `src/MSOSync.Frontend/src/features/operations/activity/components/SavedFiltersPanel.tsx`
- Create: `src/MSOSync.Frontend/src/features/operations/activity/components/EntityHistoryTab.tsx`
- Create: `src/MSOSync.Frontend/src/features/operations/activity/components/__tests__/AuditFilterBar.test.tsx`
- Modify: `src/MSOSync.Frontend/src/features/operations/activity/AuditPage.tsx`
- Modify: `src/MSOSync.Frontend/src/shared/api/audit.ts`

**Interfaces:**
- Consumes: `SyncAudit` fields: `AuditId (long)`, `Username (string?)`, `ActionName (string?)`, `ObjectName (string?)`, `CorrelationId (string?)`, `CreateTime (DateTime?)`, `TenantId (Guid)`
- Consumes: `AuditQueryService` uses `IPlatformRepository<SyncAudit>` — new method must use same
- Produces: Extended `AuditFilter` with `Usernames[]`, `ActionNames[]`, `ObjectNames[]`
- Produces: `IAuditQueryService.GetEntityHistoryAsync(string objectName, string? cursor, int pageSize, CancellationToken) → Task<CursorPageResult<AuditDto>>`
- Produces: `GET /api/v1/audit/entity/{objectName}?cursor=&pageSize=50`
- Produces: `AuditFilterBar`, `SavedFiltersPanel`, `EntityHistoryTab` components
- Produces: Enhanced `AuditPage` with 3 sub-components

**Important:** `SyncAudit` has NO `EntityType`, `EntityId`, or `Source` columns. Multi-value filter adds `Usernames[]`, `ActionNames[]`, `ObjectNames[]` only. "Entity history" filters by `ObjectName` (which is the entity identifier — e.g. node ID, username, etc.).

---

- [ ] **Step 1: Extend `AuditFilter`**

```csharp
// src/MSOSync.Metadata/Audit/AuditFilter.cs
namespace MSOSync.Metadata.Audit;

public sealed class AuditFilter
{
    // Existing single-value fields — kept for backward compatibility
    public string?   Username          { get; set; }
    public string?   ActionName        { get; set; }
    // New multi-value fields (take precedence when non-empty)
    public string[]? Usernames         { get; set; }   // OR within group
    public string[]? ActionNames       { get; set; }   // OR within group
    public string[]? ObjectNames       { get; set; }   // OR within group
    // Existing
    public DateTime? From              { get; set; }
    public DateTime? To                { get; set; }
    public string?   Cursor            { get; set; }
    public bool      IncludeTotalCount { get; set; }
    public int       PageSize          { get; set; } = 50;
}
```

- [ ] **Step 2: Extend `AuditFilterValidator`**

Open `src/MSOSync.Metadata/Audit/AuditFilterValidator.cs`. Add validation for the new array fields. The existing validator likely validates `From`/`To`/`PageSize`. Add:

```csharp
RuleFor(f => f.Usernames)
    .Must(a => a == null || a.Length <= 10)
    .WithMessage("Usernames filter cannot exceed 10 values.");

RuleFor(f => f.ActionNames)
    .Must(a => a == null || a.Length <= 10)
    .WithMessage("ActionNames filter cannot exceed 10 values.");

RuleFor(f => f.ObjectNames)
    .Must(a => a == null || a.Length <= 10)
    .WithMessage("ObjectNames filter cannot exceed 10 values.");

RuleFor(f => f)
    .Must(f =>
    {
        var total = (f.Usernames?.Length ?? 0)
                  + (f.ActionNames?.Length ?? 0)
                  + (f.ObjectNames?.Length ?? 0);
        return total <= 40;
    })
    .WithMessage("Combined filter values cannot exceed 40.");
```

- [ ] **Step 3: Add `GetEntityHistoryAsync` to `IAuditQueryService`**

Open `src/MSOSync.Metadata/Audit/IAuditQueryService.cs`. Add the new method signature:

```csharp
Task<CursorPageResult<AuditDto>> GetEntityHistoryAsync(
    string  objectName,
    string? cursor,
    int     pageSize,
    CancellationToken ct = default);
```

- [ ] **Step 4: Write failing tests**

```csharp
// tests/MSOSync.MetadataTests/Audit/AuditQueryServiceMultiFilterTests.cs
using FluentAssertions;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Pagination;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;
using Moq;
using Xunit;

namespace MSOSync.MetadataTests.Audit;

public sealed class AuditQueryServiceMultiFilterTests : IDisposable
{
    private readonly AppDbContext   _db = TestDbContext.Create();
    private readonly Mock<CursorSigner> _signer = new();

    public AuditQueryServiceMultiFilterTests()
    {
        _signer.Setup(s => s.Encode(It.IsAny<long>(), It.IsAny<long>()))
               .Returns("cursor-token");
        _signer.Setup(s => s.Decode(It.IsAny<string>()))
               .Returns((long.MaxValue, 0L));
    }

    public void Dispose() => _db.Dispose();

    private AuditQueryService BuildSvc()
        => new(new TestPlatformRepository<SyncAudit>(_db), _signer.Object);

    private async Task SeedAsync(string? username, string? actionName, string? objectName)
    {
        _db.Audits.Add(new SyncAudit
        {
            Username   = username,
            ActionName = actionName,
            ObjectName = objectName,
            CreateTime = DateTime.UtcNow,
            TenantId   = Guid.Empty,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAuditsAsync_Usernames_array_filters_by_multiple_users()
    {
        await SeedAsync("alice", "NODE_APPROVED", "n1");
        await SeedAsync("bob",   "NODE_APPROVED", "n2");
        await SeedAsync("carol", "NODE_APPROVED", "n3");

        var svc = BuildSvc();
        var result = await svc.GetAuditsAsync(new AuditFilter
        {
            Usernames = ["alice", "bob"],
            PageSize  = 50,
        });

        result.Items.Should().HaveCount(2);
        result.Items.Select(r => r.Username).Should().BeEquivalentTo(["alice", "bob"]);
    }

    [Fact]
    public async Task GetAuditsAsync_ActionNames_array_filters_by_multiple_actions()
    {
        await SeedAsync("u1", "NODE_APPROVED",  "n1");
        await SeedAsync("u2", "NODE_DISABLED",  "n2");
        await SeedAsync("u3", "NODE_HEARTBEAT", "n3");

        var svc = BuildSvc();
        var result = await svc.GetAuditsAsync(new AuditFilter
        {
            ActionNames = ["NODE_APPROVED", "NODE_DISABLED"],
            PageSize    = 50,
        });

        result.Items.Should().HaveCount(2);
        result.Items.Select(r => r.ActionName).Should()
            .BeEquivalentTo(["NODE_APPROVED", "NODE_DISABLED"]);
    }

    [Fact]
    public async Task GetAuditsAsync_ObjectNames_array_filters_by_multiple_objects()
    {
        await SeedAsync("u1", "NODE_APPROVED", "node-a");
        await SeedAsync("u2", "NODE_APPROVED", "node-b");
        await SeedAsync("u3", "NODE_APPROVED", "node-c");

        var svc = BuildSvc();
        var result = await svc.GetAuditsAsync(new AuditFilter
        {
            ObjectNames = ["node-a", "node-c"],
            PageSize    = 50,
        });

        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAuditsAsync_multi_value_takes_precedence_over_single_value()
    {
        await SeedAsync("alice", "NODE_APPROVED", "n1");
        await SeedAsync("bob",   "NODE_APPROVED", "n2");

        var svc = BuildSvc();
        var result = await svc.GetAuditsAsync(new AuditFilter
        {
            Username  = "carol",             // single-value (ignored when multi is set)
            Usernames = ["alice"],            // multi-value takes precedence
            PageSize  = 50,
        });

        result.Items.Should().HaveCount(1);
        result.Items[0].Username.Should().Be("alice");
    }

    [Fact]
    public async Task GetEntityHistoryAsync_returns_events_for_objectName()
    {
        await SeedAsync("u1", "NODE_APPROVED",  "target-node");
        await SeedAsync("u2", "NODE_DISABLED",  "target-node");
        await SeedAsync("u3", "NODE_HEARTBEAT", "other-node");

        var svc = BuildSvc();
        var result = await svc.GetEntityHistoryAsync("target-node", null, 50);

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(r => r.ObjectName == "target-node");
    }

    [Fact]
    public async Task GetEntityHistoryAsync_returns_empty_for_unknown_objectName()
    {
        var svc = BuildSvc();
        var result = await svc.GetEntityHistoryAsync("does-not-exist", null, 50);
        result.Items.Should().BeEmpty();
    }
}
```

- [ ] **Step 5: Run tests — expect failures**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~AuditQueryServiceMultiFilterTests" -v normal
```

Expected: compilation errors (new methods don't exist yet).

- [ ] **Step 6: Extend `AuditQueryService`**

Open `src/MSOSync.Metadata/Audit/AuditQueryService.cs`. The current `GetAuditsAsync` filters by single-value `Username` and `ActionName`. Update it to also handle the new multi-value arrays, then add `GetEntityHistoryAsync`:

```csharp
// Replace the existing filter application lines in GetAuditsAsync:
// Old:
//   if (filter.Username   is not null) baseQ = baseQ.Where(a => a.Username   == filter.Username);
//   if (filter.ActionName is not null) baseQ = baseQ.Where(a => a.ActionName == filter.ActionName);
// New:
        var effectiveUsernames  = filter.Usernames?.Length  > 0 ? filter.Usernames  : (filter.Username   != null ? [filter.Username]   : null);
        var effectiveActions    = filter.ActionNames?.Length > 0 ? filter.ActionNames : (filter.ActionName != null ? [filter.ActionName] : null);
        var effectiveObjectNames = filter.ObjectNames?.Length > 0 ? filter.ObjectNames : null;

        if (effectiveUsernames   is not null) baseQ = baseQ.Where(a => effectiveUsernames.Contains(a.Username));
        if (effectiveActions     is not null) baseQ = baseQ.Where(a => effectiveActions.Contains(a.ActionName));
        if (effectiveObjectNames is not null) baseQ = baseQ.Where(a => effectiveObjectNames.Contains(a.ObjectName));
        if (filter.From          is not null) baseQ = baseQ.Where(a => a.CreateTime >= filter.From);
        if (filter.To            is not null) baseQ = baseQ.Where(a => a.CreateTime <= filter.To);
```

Add `GetEntityHistoryAsync` at the end of the class body:

```csharp
    public async Task<CursorPageResult<AuditDto>> GetEntityHistoryAsync(
        string  objectName,
        string? cursor,
        int     pageSize,
        CancellationToken ct = default)
    {
        var baseQ = auditRepo.QueryAll()
            .Where(a => a.CreateTime != null && a.ObjectName == objectName);

        var q = baseQ;
        if (cursor is not null)
        {
            var (cursorId, _) = cursorSigner.Decode(cursor);
            q = q.Where(a => a.AuditId < cursorId);
        }

        var size = Math.Clamp(pageSize, 1, 200);
        var rows = await q
            .OrderByDescending(a => a.AuditId)
            .Take(size + 1)
            .Select(a => new AuditDto(
                a.AuditId, a.Username, a.ActionName,
                a.ObjectName, a.CorrelationId, a.CreateTime!.Value))
            .ToListAsync(ct);

        var hasMore = rows.Count > size;
        if (hasMore) rows = rows.Take(size).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = rows[^1];
            nextCursor = cursorSigner.Encode(last.AuditId, last.CreateTime.Ticks);
        }

        return new CursorPageResult<AuditDto>(rows.AsReadOnly(), nextCursor, hasMore, null);
    }
```

- [ ] **Step 7: Run tests — expect pass**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~AuditQueryServiceMultiFilterTests" -v normal
```

Expected: all 6 tests PASS.

- [ ] **Step 8: Add entity history endpoint to `AuditController`**

Open `src/MSOSync.Api/Controllers/AuditController.cs`. Add at the end of the class body:

```csharp
    // GET /api/v1/audit/entity/{objectName}?cursor=&pageSize=50
    [HttpGet("entity/{objectName}")]
    [ProducesResponseType(typeof(CursorPageResult<AuditDto>), 200)]
    public async Task<IActionResult> GetEntityHistory(
        string  objectName,
        [FromQuery] string? cursor,
        [FromQuery] int     pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await audit.GetEntityHistoryAsync(objectName, cursor, pageSize, ct);
        return Ok(result);
    }
```

- [ ] **Step 9: Build backend**

```
dotnet build src/MSOSync.Api/MSOSync.Api.csproj
```

Expected: 0 errors.

- [ ] **Step 10: Create `AuditFilterBar.tsx`**

```tsx
// src/MSOSync.Frontend/src/features/operations/activity/components/AuditFilterBar.tsx
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
```

- [ ] **Step 11: Create `SavedFiltersPanel.tsx`**

```tsx
// src/MSOSync.Frontend/src/features/operations/activity/components/SavedFiltersPanel.tsx
import { Trash2 } from 'lucide-react';
import type { AuditFilterState } from './AuditFilterBar';

interface SavedFilter {
  name:   string;
  filter: AuditFilterState;
}

interface Props {
  filters:  SavedFilter[];
  onLoad:   (f: AuditFilterState) => void;
  onDelete: (name: string) => void;
}

export function SavedFiltersPanel({ filters, onLoad, onDelete }: Props) {
  if (filters.length === 0) {
    return <p className="text-xs text-muted-foreground p-2">No saved filters.</p>;
  }
  return (
    <div className="space-y-1">
      {filters.map(sf => (
        <div key={sf.name} className="flex items-center justify-between rounded px-2 py-1.5 hover:bg-muted/50 group">
          <button
            className="text-sm text-left flex-1 truncate"
            onClick={() => onLoad(sf.filter)}
          >
            {sf.name}
          </button>
          <button
            className="opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-destructive"
            onClick={() => onDelete(sf.name)}
          >
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        </div>
      ))}
    </div>
  );
}
```

- [ ] **Step 12: Create `EntityHistoryTab.tsx`**

```tsx
// src/MSOSync.Frontend/src/features/operations/activity/components/EntityHistoryTab.tsx
import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { DataGrid } from '@/shared/components/data-display/DataGrid';
import client from '@/shared/api/client';
import type { ColDef } from 'ag-grid-community';
import type { AuditDto } from '@/shared/types/audit';
import { formatDistanceToNow } from 'date-fns';

function useEntityHistory(objectName: string | null) {
  return useQuery({
    queryKey:  ['audit', 'entity', objectName],
    queryFn:   async ({ signal }) => {
      const { data } = await client.get(
        `/audit/entity/${encodeURIComponent(objectName!)}`,
        { params: { pageSize: 100 }, signal },
      );
      return data as { items: AuditDto[]; hasMore: boolean };
    },
    enabled:   objectName !== null && objectName.trim() !== '',
    staleTime: 30_000,
  });
}

const COLUMNS: ColDef<AuditDto>[] = [
  { field: 'createTime', headerName: 'Time', width: 180,
    valueFormatter: p => p.value ? formatDistanceToNow(new Date(p.value), { addSuffix: true }) : '' },
  { field: 'actionName',   headerName: 'Action',   flex: 1 },
  { field: 'username',     headerName: 'By',        width: 140 },
  { field: 'correlationId', headerName: 'Correlation', flex: 1 },
];

export function EntityHistoryTab() {
  const [inputValue, setInputValue]   = useState('');
  const [objectName, setObjectName]   = useState<string | null>(null);

  const { data, isFetching } = useEntityHistory(objectName);

  return (
    <div className="space-y-4 p-4">
      <div className="flex items-center gap-3">
        <div className="space-y-1 flex-1 max-w-xs">
          <label className="text-xs text-muted-foreground">Object Name (node ID, username, etc.)</label>
          <input
            className="w-full rounded border bg-background px-3 py-1.5 text-sm"
            placeholder="e.g. node-01 or alice"
            value={inputValue}
            onChange={e => setInputValue(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') setObjectName(inputValue.trim() || null); }}
          />
        </div>
        <Button
          className="mt-5"
          onClick={() => setObjectName(inputValue.trim() || null)}
        >
          Load
        </Button>
      </div>

      {objectName && (
        isFetching ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : !data || data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">No audit events found for "{objectName}".</p>
        ) : (
          <>
            {data.hasMore && (
              <p className="text-xs text-muted-foreground">Showing first 100 events. Narrow date range for full history.</p>
            )}
            <div className="h-[400px]">
              <DataGrid<AuditDto>
                rowData={data.items}
                columnDefs={COLUMNS}
                rowId="auditId"
              />
            </div>
          </>
        )
      )}
    </div>
  );
}
```

- [ ] **Step 13: Write `AuditFilterBar` test**

```typescript
// src/MSOSync.Frontend/src/features/operations/activity/components/__tests__/AuditFilterBar.test.tsx
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AuditFilterBar, type AuditFilterState } from '../AuditFilterBar';

const empty: AuditFilterState = {
  usernames: [], actionNames: [], objectNames: [], from: '', to: '',
};

describe('AuditFilterBar', () => {
  it('renders all filter sections', () => {
    render(
      <AuditFilterBar value={empty} onChange={vi.fn()} onSave={vi.fn()} />,
    );
    expect(screen.getByText('Usernames')).toBeInTheDocument();
    expect(screen.getByText('Actions')).toBeInTheDocument();
    expect(screen.getByText('Object Names')).toBeInTheDocument();
  });

  it('shows Clear All when filter is active', () => {
    const active: AuditFilterState = { ...empty, usernames: ['alice'] };
    render(
      <AuditFilterBar value={active} onChange={vi.fn()} onSave={vi.fn()} />,
    );
    expect(screen.getByRole('button', { name: /clear all/i })).toBeInTheDocument();
  });

  it('calls onChange with empty filter on Clear All click', () => {
    const onChange = vi.fn();
    const active: AuditFilterState = { ...empty, usernames: ['alice'] };
    render(
      <AuditFilterBar value={active} onChange={onChange} onSave={vi.fn()} />,
    );
    fireEvent.click(screen.getByRole('button', { name: /clear all/i }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ usernames: [] }));
  });

  it('does not show Clear All when filter is empty', () => {
    render(
      <AuditFilterBar value={empty} onChange={vi.fn()} onSave={vi.fn()} />,
    );
    expect(screen.queryByRole('button', { name: /clear all/i })).not.toBeInTheDocument();
  });

  it('shows chip for existing username value', () => {
    const active: AuditFilterState = { ...empty, usernames: ['alice'] };
    render(
      <AuditFilterBar value={active} onChange={vi.fn()} onSave={vi.fn()} />,
    );
    expect(screen.getByText('alice')).toBeInTheDocument();
  });
});
```

- [ ] **Step 14: Run frontend tests**

```
cd src/MSOSync.Frontend && npm test -- AuditFilterBar
```

Expected: 5 tests PASS.

- [ ] **Step 15: Integrate into `AuditPage.tsx`**

Open `src/MSOSync.Frontend/src/features/operations/activity/AuditPage.tsx`. Add the new components to the existing page structure. The AuditPage likely already has tabs for "Audit Log", "Insights", "Correlation Timeline". Add:
1. Replace simple filter inputs at top with `AuditFilterBar`
2. Add `SavedFiltersPanel` in a collapsible sidebar or dropdown
3. Add "Entity History" as a new tab

```tsx
// Add imports at top of AuditPage.tsx:
import { useState } from 'react';
import { AuditFilterBar, type AuditFilterState } from './components/AuditFilterBar';
import { SavedFiltersPanel } from './components/SavedFiltersPanel';
import { EntityHistoryTab } from './components/EntityHistoryTab';
import { usePreferencesService } from '@/shared/hooks/usePreferences';

// Saved filter persistence helpers (add inside component or as module-level):
const SAVED_FILTER_PREF_KEY = 'audit.savedFilters';

interface SavedFilter { name: string; filter: AuditFilterState; }
```

Wire the filter bar to the existing audit query by mapping `AuditFilterState` → query params:
```tsx
// Inside AuditPage component:
const [filterState, setFilterState] = useState<AuditFilterState>({
  usernames: [], actionNames: [], objectNames: [], from: '', to: '',
});
const [savedFilters, setSavedFilters] = useState<SavedFilter[]>([]);

// Map AuditFilterState to the existing audit query params:
const auditParams = {
  usernames:   filterState.usernames.length   > 0 ? filterState.usernames   : undefined,
  actionNames: filterState.actionNames.length > 0 ? filterState.actionNames : undefined,
  objectNames: filterState.objectNames.length > 0 ? filterState.objectNames : undefined,
  from:        filterState.from || undefined,
  to:          filterState.to   || undefined,
};
```

Load saved filters on mount and persist via preferences:
```tsx
// Load saved filters from preferences (call once on mount):
useEffect(() => {
  preferencesService.getKey(SAVED_FILTER_PREF_KEY).then(raw => {
    if (raw) setSavedFilters(JSON.parse(JSON.stringify(raw)));
  }).catch(() => {});
}, []);

const handleSaveFilter = (name: string) => {
  const updated = [...savedFilters.filter(f => f.name !== name), { name, filter: filterState }];
  setSavedFilters(updated);
  void preferencesService.set(SAVED_FILTER_PREF_KEY, updated);
};

const handleDeleteFilter = (name: string) => {
  const updated = savedFilters.filter(f => f.name !== name);
  setSavedFilters(updated);
  void preferencesService.set(SAVED_FILTER_PREF_KEY, updated);
};
```

Replace whatever filter bar exists with:
```tsx
<AuditFilterBar
  value={filterState}
  onChange={setFilterState}
  onSave={handleSaveFilter}
  knownActions={KNOWN_AUDIT_ACTIONS}  // define this as a const array of common action names
/>
{savedFilters.length > 0 && (
  <SavedFiltersPanel
    filters={savedFilters}
    onLoad={setFilterState}
    onDelete={handleDeleteFilter}
  />
)}
```

Add "Entity History" as a new tab next to existing tabs:
```tsx
// In the tabs definition array (wherever existing tabs are defined):
{ key: 'entity-history', label: 'Entity History', component: <EntityHistoryTab /> }
```

> **Note:** The exact integration depends on how `AuditPage.tsx` is currently structured. Look at where the tab list is defined and where the current filter inputs are. Adapt the above pattern to fit the existing component structure. Do not restructure the component — only add the new pieces.

- [ ] **Step 16: Build frontend**

```
cd src/MSOSync.Frontend && npm run build
```

Expected: 0 TypeScript errors.

- [ ] **Step 17: Build full solution**

```
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: 0 errors.

- [ ] **Step 18: Commit**

```
git add src/MSOSync.Metadata/Audit/AuditFilter.cs
git add src/MSOSync.Metadata/Audit/AuditFilterValidator.cs
git add src/MSOSync.Metadata/Audit/IAuditQueryService.cs
git add src/MSOSync.Metadata/Audit/AuditQueryService.cs
git add src/MSOSync.Api/Controllers/AuditController.cs
git add tests/MSOSync.MetadataTests/Audit/AuditQueryServiceMultiFilterTests.cs
git add src/MSOSync.Frontend/src/features/operations/activity/components/AuditFilterBar.tsx
git add src/MSOSync.Frontend/src/features/operations/activity/components/SavedFiltersPanel.tsx
git add src/MSOSync.Frontend/src/features/operations/activity/components/EntityHistoryTab.tsx
git add "src/MSOSync.Frontend/src/features/operations/activity/components/__tests__/AuditFilterBar.test.tsx"
git add src/MSOSync.Frontend/src/features/operations/activity/AuditPage.tsx
git add src/MSOSync.Frontend/src/shared/api/audit.ts
git commit -m "feat(2B.3-T3): Audit Explorer — multi-value filter, entity history endpoint, AuditFilterBar"
```
