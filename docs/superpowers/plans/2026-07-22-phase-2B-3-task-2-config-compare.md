# Task 2 — Configuration Comparison

**Files:**
- Create: `src/MSOSync.Metadata/Configuration/JsonDiffEngine.cs`
- Create: `src/MSOSync.Metadata/Configuration/Dtos/ConfigVersionDiffDto.cs`
- Create: `src/MSOSync.Metadata/Configuration/IConfigurationComparisonService.cs`
- Create: `src/MSOSync.Metadata/Configuration/ConfigurationComparisonService.cs`
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- Modify: `src/MSOSync.Api/Controllers/ConfigurationTemplateController.cs`
- Create: `tests/MSOSync.MetadataTests/Configuration/JsonDiffEngineTests.cs`
- Create: `tests/MSOSync.MetadataTests/Configuration/ConfigurationComparisonServiceTests.cs`
- Create: `src/MSOSync.Frontend/src/shared/api/configComparison.ts`
- Create: `src/MSOSync.Frontend/src/shared/hooks/useConfigComparison.ts`
- Create: `src/MSOSync.Frontend/src/features/operations/configuration/components/ConfigComparePanel.tsx`
- Create: `src/MSOSync.Frontend/src/features/operations/configuration/components/__tests__/ConfigComparePanel.test.tsx`
- Modify: `src/MSOSync.Frontend/src/features/operations/configuration/TemplatesPage.tsx` (or wherever version history is shown)

**Interfaces:**
- Consumes: `SyncConfigurationTemplateVersion.SettingsJson` (string), `SyncConfigurationTemplateVersion.VersionNumber` (int), `SyncConfigurationTemplateVersion.IsDraft` (bool), `SyncConfigurationTemplateVersion.PublishedAt` (DateTime?)
- Produces: `IConfigurationComparisonService.CompareAsync(Guid templateId, int v1, int v2, CancellationToken) → Task<ConfigVersionDiffDto>`
- Produces: `GET /api/v1/configuration/templates/{id}/compare?v1=&v2=` → 200 `ConfigVersionDiffDto` | 400 | 404
- Produces: `useConfigComparison(templateId, v1, v2)` hook
- Produces: `ConfigComparePanel` component

---

- [ ] **Step 1: Create response DTOs**

```csharp
// src/MSOSync.Metadata/Configuration/Dtos/ConfigVersionDiffDto.cs
namespace MSOSync.Metadata.Configuration.Dtos;

public sealed record ConfigVersionDiffDto(
    Guid                        TemplateId,
    int                         V1,
    int                         V2,
    string                      V1Label,
    string                      V2Label,
    IReadOnlyList<DiffEntryDto> Entries,
    bool                        HasChanges);

public sealed record DiffEntryDto(
    string  Key,
    string  ChangeType,   // "Added" | "Removed" | "Changed" | "Unchanged"
    string? OldValue,
    string? NewValue);
```

- [ ] **Step 2: Write failing `JsonDiffEngine` tests**

```csharp
// tests/MSOSync.MetadataTests/Configuration/JsonDiffEngineTests.cs
using System.Text.Json;
using FluentAssertions;
using MSOSync.Metadata.Configuration;
using Xunit;

namespace MSOSync.MetadataTests.Configuration;

public sealed class JsonDiffEngineTests
{
    private static IReadOnlyList<DiffEntryDto> Diff(string json1, string json2)
    {
        var doc1 = JsonDocument.Parse(json1);
        var doc2 = JsonDocument.Parse(json2);
        return JsonDiffEngine.Diff(doc1.RootElement, doc2.RootElement);
    }

    [Fact]
    public void Diff_identical_json_returns_all_unchanged()
    {
        var json = """{"host":"server01","port":5432}""";
        var entries = Diff(json, json);
        entries.Should().OnlyContain(e => e.ChangeType == "Unchanged");
    }

    [Fact]
    public void Diff_detects_changed_value()
    {
        var json1 = """{"host":"server01"}""";
        var json2 = """{"host":"server02"}""";
        var entries = Diff(json1, json2);
        entries.Should().ContainSingle(e => e.ChangeType == "Changed" && e.Key == "host"
            && e.OldValue == "server01" && e.NewValue == "server02");
    }

    [Fact]
    public void Diff_detects_added_key()
    {
        var json1 = """{"host":"server01"}""";
        var json2 = """{"host":"server01","port":5432}""";
        var entries = Diff(json1, json2);
        entries.Should().Contain(e => e.ChangeType == "Added" && e.Key == "port" && e.NewValue == "5432");
    }

    [Fact]
    public void Diff_detects_removed_key()
    {
        var json1 = """{"host":"server01","port":5432}""";
        var json2 = """{"host":"server01"}""";
        var entries = Diff(json1, json2);
        entries.Should().Contain(e => e.ChangeType == "Removed" && e.Key == "port" && e.OldValue == "5432");
    }

    [Fact]
    public void Diff_flattens_nested_objects_with_dot_notation()
    {
        var json1 = """{"database":{"host":"s1","port":1433}}""";
        var json2 = """{"database":{"host":"s2","port":1433}}""";
        var entries = Diff(json1, json2);
        entries.Should().Contain(e => e.Key == "database.host" && e.ChangeType == "Changed");
        entries.Should().Contain(e => e.Key == "database.port" && e.ChangeType == "Unchanged");
    }

    [Fact]
    public void Diff_treats_arrays_as_atomic()
    {
        var json1 = """{"tags":["a","b"]}""";
        var json2 = """{"tags":["a","c"]}""";
        var entries = Diff(json1, json2);
        entries.Should().ContainSingle(e => e.Key == "tags" && e.ChangeType == "Changed");
    }

    [Fact]
    public void Diff_sorts_changed_first_then_added_then_removed_then_unchanged()
    {
        var json1 = """{"a":"1","b":"2","c":"3"}""";
        var json2 = """{"a":"X","d":"4","c":"3"}""";
        var entries = Diff(json1, json2);
        var types = entries.Select(e => e.ChangeType).ToList();
        var firstChanged = types.IndexOf("Changed");
        var firstAdded   = types.IndexOf("Added");
        var firstRemoved = types.IndexOf("Removed");
        var firstUnchanged = types.IndexOf("Unchanged");
        firstChanged.Should().BeLessThan(firstAdded);
        firstAdded.Should().BeLessThan(firstRemoved);
        firstRemoved.Should().BeLessThan(firstUnchanged);
    }
}
```

- [ ] **Step 3: Run JsonDiffEngine tests — expect failures**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~JsonDiffEngineTests" -v normal
```

Expected: compilation errors (JsonDiffEngine doesn't exist).

- [ ] **Step 4: Implement `JsonDiffEngine`**

```csharp
// src/MSOSync.Metadata/Configuration/JsonDiffEngine.cs
using System.Text.Json;
using MSOSync.Metadata.Configuration.Dtos;

namespace MSOSync.Metadata.Configuration;

internal static class JsonDiffEngine
{
    public static IReadOnlyList<DiffEntryDto> Diff(JsonElement v1Root, JsonElement v2Root)
    {
        var map1 = Flatten(v1Root, "");
        var map2 = Flatten(v2Root, "");

        var allKeys = new HashSet<string>(map1.Keys.Concat(map2.Keys));
        var entries = new List<DiffEntryDto>(allKeys.Count);

        foreach (var key in allKeys)
        {
            var has1 = map1.TryGetValue(key, out var val1);
            var has2 = map2.TryGetValue(key, out var val2);

            var changeType = (has1, has2) switch
            {
                (true, false) => "Removed",
                (false, true) => "Added",
                _             => val1 == val2 ? "Unchanged" : "Changed",
            };

            entries.Add(new DiffEntryDto(key, changeType, has1 ? val1 : null, has2 ? val2 : null));
        }

        return entries
            .OrderBy(e => e.ChangeType switch
            {
                "Changed"   => 0,
                "Added"     => 1,
                "Removed"   => 2,
                _           => 3,
            })
            .ThenBy(e => e.Key)
            .ToList()
            .AsReadOnly();
    }

    private static Dictionary<string, string> Flatten(JsonElement element, string prefix)
    {
        var result = new Dictionary<string, string>();
        FlattenInto(element, prefix, result);
        return result;
    }

    private static void FlattenInto(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                FlattenInto(prop.Value, key, result);
            }
        }
        else
        {
            // Arrays and scalars are treated as atomic string values
            result[prefix] = element.ValueKind == JsonValueKind.String
                ? element.GetString()!
                : element.GetRawText();
        }
    }
}
```

- [ ] **Step 5: Run JsonDiffEngine tests — expect pass**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~JsonDiffEngineTests" -v normal
```

Expected: all 7 tests PASS.

- [ ] **Step 6: Create service interface**

```csharp
// src/MSOSync.Metadata/Configuration/IConfigurationComparisonService.cs
using MSOSync.Metadata.Configuration.Dtos;

namespace MSOSync.Metadata.Configuration;

public interface IConfigurationComparisonService
{
    Task<ConfigVersionDiffDto> CompareAsync(
        Guid templateId, int v1, int v2, CancellationToken ct = default);
}
```

- [ ] **Step 7: Write failing service tests**

```csharp
// tests/MSOSync.MetadataTests/Configuration/ConfigurationComparisonServiceTests.cs
using FluentAssertions;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Configuration;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Configuration;

public sealed class ConfigurationComparisonServiceTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContext.Create();

    public void Dispose() => _db.Dispose();

    private async Task<Guid> SeedTemplateAsync(string name)
    {
        var id = Guid.NewGuid();
        _db.ConfigurationTemplates.Add(new SyncConfigurationTemplate
        {
            Id = id, Name = name, Description = name,
            LatestDraftVersion = null, CurrentPublishedVersion = null,
            TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task SeedVersionAsync(Guid templateId, int version, string settingsJson, bool isDraft = false)
    {
        _db.ConfigurationTemplateVersions.Add(new SyncConfigurationTemplateVersion
        {
            Id = Guid.NewGuid(), TemplateId = templateId,
            VersionNumber = version, SettingsJson = settingsJson,
            IsDraft = isDraft, SchemaVersion = 1,
            TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task CompareAsync_returns_diff_for_valid_versions()
    {
        var templateId = await SeedTemplateAsync("t1");
        await SeedVersionAsync(templateId, 1, """{"host":"s1"}""");
        await SeedVersionAsync(templateId, 2, """{"host":"s2"}""");

        var svc = new ConfigurationComparisonService(_db);
        var result = await svc.CompareAsync(templateId, 1, 2);

        result.TemplateId.Should().Be(templateId);
        result.V1.Should().Be(1);
        result.V2.Should().Be(2);
        result.HasChanges.Should().BeTrue();
        result.Entries.Should().Contain(e => e.Key == "host" && e.ChangeType == "Changed");
    }

    [Fact]
    public async Task CompareAsync_throws_NotFoundException_when_version_missing()
    {
        var templateId = await SeedTemplateAsync("t2");
        await SeedVersionAsync(templateId, 1, """{"host":"s1"}""");

        var svc = new ConfigurationComparisonService(_db);
        var act = async () => await svc.CompareAsync(templateId, 1, 99);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CompareAsync_HasChanges_false_when_identical()
    {
        var templateId = await SeedTemplateAsync("t3");
        await SeedVersionAsync(templateId, 1, """{"host":"s1"}""");
        await SeedVersionAsync(templateId, 2, """{"host":"s1"}""");

        var svc = new ConfigurationComparisonService(_db);
        var result = await svc.CompareAsync(templateId, 1, 2);
        result.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task CompareAsync_generates_readable_version_labels()
    {
        var templateId = await SeedTemplateAsync("t4");
        await SeedVersionAsync(templateId, 1, """{}""", isDraft: false);
        await SeedVersionAsync(templateId, 2, """{}""", isDraft: true);

        var svc = new ConfigurationComparisonService(_db);
        var result = await svc.CompareAsync(templateId, 1, 2);
        result.V1Label.Should().Contain("v1");
        result.V2Label.Should().Contain("v2");
        result.V2Label.Should().Contain("draft", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompareAsync_throws_NotFoundException_when_version_belongs_to_different_template()
    {
        var templateId1 = await SeedTemplateAsync("t5a");
        var templateId2 = await SeedTemplateAsync("t5b");
        await SeedVersionAsync(templateId1, 1, """{}""");
        await SeedVersionAsync(templateId2, 2, """{}"""); // different template

        var svc = new ConfigurationComparisonService(_db);
        var act = async () => await svc.CompareAsync(templateId1, 1, 2);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] **Step 8: Run service tests — expect failures**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~ConfigurationComparisonServiceTests" -v normal
```

Expected: compilation errors (service doesn't exist).

- [ ] **Step 9: Implement `ConfigurationComparisonService`**

```csharp
// src/MSOSync.Metadata/Configuration/ConfigurationComparisonService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Configuration.Dtos;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Configuration;

public sealed class ConfigurationComparisonService(AppDbContext db) : IConfigurationComparisonService
{
    public async Task<ConfigVersionDiffDto> CompareAsync(
        Guid templateId, int v1, int v2, CancellationToken ct = default)
    {
        var versions = await db.ConfigurationTemplateVersions
            .AsNoTracking()
            .Where(v => v.TemplateId == templateId && (v.VersionNumber == v1 || v.VersionNumber == v2))
            .Select(v => new { v.VersionNumber, v.SettingsJson, v.IsDraft, v.PublishedAt })
            .ToListAsync(ct);

        var ver1 = versions.FirstOrDefault(v => v.VersionNumber == v1)
            ?? throw new NotFoundException($"Version {v1} not found for template {templateId}", "VERSION_NOT_FOUND");
        var ver2 = versions.FirstOrDefault(v => v.VersionNumber == v2)
            ?? throw new NotFoundException($"Version {v2} not found for template {templateId}", "VERSION_NOT_FOUND");

        var doc1 = JsonDocument.Parse(ver1.SettingsJson ?? "{}");
        var doc2 = JsonDocument.Parse(ver2.SettingsJson ?? "{}");
        var entries = JsonDiffEngine.Diff(doc1.RootElement, doc2.RootElement);

        return new ConfigVersionDiffDto(
            TemplateId: templateId,
            V1:         v1,
            V2:         v2,
            V1Label:    BuildLabel(v1, ver1.IsDraft, ver1.PublishedAt),
            V2Label:    BuildLabel(v2, ver2.IsDraft, ver2.PublishedAt),
            Entries:    entries,
            HasChanges: entries.Any(e => e.ChangeType != "Unchanged"));
    }

    private static string BuildLabel(int version, bool isDraft, DateTime? publishedAt)
    {
        if (isDraft) return $"v{version} (Draft)";
        return publishedAt.HasValue
            ? $"v{version} (Published {publishedAt.Value:yyyy-MM-dd})"
            : $"v{version}";
    }
}
```

- [ ] **Step 10: Run service tests — expect pass**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~ConfigurationComparisonServiceTests" -v normal
```

Expected: all 5 tests PASS.

- [ ] **Step 11: Register service and add compare endpoint to controller**

In `MetadataServiceExtensions.cs`, add to Phase 2B.3 block:
```csharp
        services.AddScoped<IConfigurationComparisonService, ConfigurationComparisonService>();
```

In `src/MSOSync.Api/Controllers/ConfigurationTemplateController.cs`:

Add `IConfigurationComparisonService comparisonSvc` to the primary constructor parameters:
```csharp
public sealed class ConfigurationTemplateController(
    IConfigurationTemplateService    templateSvc,
    INodeAuthorizationService        authz,
    IConfigurationComparisonService  comparisonSvc,
    IValidator<CreateTemplateRequest> createValidator,
    IValidator<UpdateDraftRequest>   updateValidator) : ControllerBase
```

Add using statements at the top:
```csharp
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Configuration;
using MSOSync.Metadata.Configuration.Dtos;
```

Add the compare endpoint method at the end of the controller class:
```csharp
    [HttpGet("{id:guid}/compare")]
    [ProducesResponseType(typeof(ConfigVersionDiffDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Compare(
        Guid id,
        [FromQuery] int v1,
        [FromQuery] int v2,
        CancellationToken ct)
    {
        await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
        if (v1 == v2) return BadRequest(new ProblemDetails { Title = "v1 and v2 must differ." });
        var diff = await comparisonSvc.CompareAsync(id, v1, v2, ct);
        return Ok(diff);
    }
```

Note: The `GlobalExceptionHandler` already handles `NotFoundException` → 404, so no try/catch needed.

- [ ] **Step 12: Build backend**

```
dotnet build src/MSOSync.Api/MSOSync.Api.csproj
```

Expected: 0 errors.

- [ ] **Step 13: Create frontend API function**

```typescript
// src/MSOSync.Frontend/src/shared/api/configComparison.ts
import client from './client';
import type { ConfigVersionDiffDto } from '../types/configComparison';

export const configCompareKeys = {
  diff: (templateId: string, v1: number, v2: number) =>
    ['config-compare', templateId, v1, v2] as const,
} as const;

export async function getConfigVersionDiff(
  templateId: string,
  v1: number,
  v2: number,
  options?: { signal?: AbortSignal },
): Promise<ConfigVersionDiffDto> {
  const { data } = await client.get<ConfigVersionDiffDto>(
    `/configuration/templates/${encodeURIComponent(templateId)}/compare`,
    { params: { v1, v2 }, ...options },
  );
  return data;
}
```

- [ ] **Step 14: Create TypeScript types**

```typescript
// src/MSOSync.Frontend/src/shared/types/configComparison.ts
export type ChangeType = 'Added' | 'Removed' | 'Changed' | 'Unchanged';

export interface DiffEntryDto {
  key: string;
  changeType: ChangeType;
  oldValue: string | null;
  newValue: string | null;
}

export interface ConfigVersionDiffDto {
  templateId: string;
  v1: number;
  v2: number;
  v1Label: string;
  v2Label: string;
  entries: DiffEntryDto[];
  hasChanges: boolean;
}
```

- [ ] **Step 15: Create hook**

```typescript
// src/MSOSync.Frontend/src/shared/hooks/useConfigComparison.ts
import { useQuery } from '@tanstack/react-query';
import { configCompareKeys, getConfigVersionDiff } from '../api/configComparison';

export function useConfigComparison(
  templateId: string | null,
  v1: number | null,
  v2: number | null,
) {
  return useQuery({
    queryKey: configCompareKeys.diff(templateId ?? '', v1 ?? 0, v2 ?? 0),
    queryFn:  ({ signal }) => getConfigVersionDiff(templateId!, v1!, v2!, { signal }),
    enabled:  templateId !== null && v1 !== null && v2 !== null && v1 !== v2,
    staleTime: 60_000,
  });
}
```

- [ ] **Step 16: Create `ConfigComparePanel.tsx`**

```tsx
// src/MSOSync.Frontend/src/features/operations/configuration/components/ConfigComparePanel.tsx
import { useState } from 'react';
import { useConfigComparison } from '@/shared/hooks/useConfigComparison';
import { Button } from '@/components/ui/button';
import { X } from 'lucide-react';
import type { ChangeType } from '@/shared/types/configComparison';

interface Props {
  templateId: string;
  availableVersions: { versionNumber: number; label: string }[];
  onClose: () => void;
}

const ROW_COLOR: Record<ChangeType, string> = {
  Changed:   'bg-yellow-50 dark:bg-yellow-950/20',
  Added:     'bg-green-50  dark:bg-green-950/20',
  Removed:   'bg-red-50    dark:bg-red-950/20',
  Unchanged: '',
};

const BADGE_COLOR: Record<ChangeType, string> = {
  Changed:   'text-yellow-700 bg-yellow-100',
  Added:     'text-green-700  bg-green-100',
  Removed:   'text-red-700    bg-red-100',
  Unchanged: 'text-gray-500   bg-gray-100',
};

export function ConfigComparePanel({ templateId, availableVersions, onClose }: Props) {
  const [v1, setV1] = useState<number | null>(null);
  const [v2, setV2] = useState<number | null>(null);
  const [showUnchanged, setShowUnchanged] = useState(false);

  const { data, isFetching, error } = useConfigComparison(templateId, v1, v2);

  const unchangedCount = data?.entries.filter(e => e.ChangeType === 'Unchanged').length ?? 0;
  const visibleEntries = showUnchanged
    ? data?.entries
    : data?.entries.filter(e => e.changeType !== 'Unchanged');

  return (
    <div className="fixed inset-y-0 right-0 z-50 w-[680px] border-l bg-background shadow-xl flex flex-col">
      {/* Header */}
      <div className="flex items-center justify-between border-b px-4 py-3">
        <h2 className="font-semibold text-sm">Compare Template Versions</h2>
        <Button variant="ghost" size="icon" onClick={onClose}><X className="h-4 w-4" /></Button>
      </div>

      {/* Version pickers */}
      <div className="flex items-center gap-3 border-b px-4 py-3">
        <div className="flex-1 space-y-1">
          <label className="text-xs text-muted-foreground">From version (V1)</label>
          <select
            className="w-full rounded border bg-background px-2 py-1.5 text-sm"
            value={v1 ?? ''}
            onChange={e => setV1(e.target.value ? Number(e.target.value) : null)}
          >
            <option value="">Select…</option>
            {availableVersions.map(v => (
              <option key={v.versionNumber} value={v.versionNumber}>{v.label}</option>
            ))}
          </select>
        </div>
        <span className="mt-5 text-muted-foreground">→</span>
        <div className="flex-1 space-y-1">
          <label className="text-xs text-muted-foreground">To version (V2)</label>
          <select
            className="w-full rounded border bg-background px-2 py-1.5 text-sm"
            value={v2 ?? ''}
            onChange={e => setV2(e.target.value ? Number(e.target.value) : null)}
          >
            <option value="">Select…</option>
            {availableVersions.map(v => (
              <option key={v.versionNumber} value={v.versionNumber}>{v.label}</option>
            ))}
          </select>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto">
        {!v1 || !v2 ? (
          <div className="p-6 text-sm text-muted-foreground">Select two different versions to compare.</div>
        ) : v1 === v2 ? (
          <div className="p-6 text-sm text-muted-foreground">V1 and V2 must be different versions.</div>
        ) : isFetching ? (
          <div className="p-6 text-sm text-muted-foreground">Loading diff…</div>
        ) : error ? (
          <div className="p-6 text-sm text-destructive">Failed to load diff.</div>
        ) : !data ? null : data.entries.length === 0 ? (
          <div className="p-6 text-sm text-muted-foreground">No differences found.</div>
        ) : (
          <>
            {/* Summary */}
            <div className="flex items-center gap-3 px-4 py-2 border-b text-xs text-muted-foreground bg-muted/30">
              <span>{data.v1Label}</span>
              <span>→</span>
              <span>{data.v2Label}</span>
              {!data.hasChanges && <span className="ml-auto text-green-600">No differences</span>}
            </div>

            {/* Diff table */}
            <table className="w-full text-xs">
              <thead className="sticky top-0 bg-muted/80 border-b">
                <tr>
                  <th className="px-3 py-2 text-left font-medium w-1/3">Key</th>
                  <th className="px-3 py-2 text-left font-medium w-16">Change</th>
                  <th className="px-3 py-2 text-left font-medium">Old Value</th>
                  <th className="px-3 py-2 text-left font-medium">New Value</th>
                </tr>
              </thead>
              <tbody>
                {visibleEntries?.map((entry, i) => (
                  <tr key={i} className={`border-b ${ROW_COLOR[entry.changeType]}`}>
                    <td className="px-3 py-2 font-mono font-medium">{entry.key}</td>
                    <td className="px-3 py-2">
                      <span className={`rounded px-1.5 py-0.5 text-[10px] font-semibold ${BADGE_COLOR[entry.changeType]}`}>
                        {entry.changeType}
                      </span>
                    </td>
                    <td className="px-3 py-2 font-mono text-muted-foreground">
                      {entry.oldValue ?? '—'}
                    </td>
                    <td className="px-3 py-2 font-mono">
                      {entry.newValue ?? '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {/* Show unchanged toggle */}
            {unchangedCount > 0 && (
              <div className="px-4 py-2 border-t">
                <button
                  className="text-xs text-muted-foreground hover:text-foreground underline"
                  onClick={() => setShowUnchanged(prev => !prev)}
                >
                  {showUnchanged ? `Hide ${unchangedCount} unchanged` : `Show ${unchangedCount} unchanged`}
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 17: Write frontend test**

```typescript
// src/MSOSync.Frontend/src/features/operations/configuration/components/__tests__/ConfigComparePanel.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConfigComparePanel } from '../ConfigComparePanel';
import * as api from '@/shared/api/configComparison';
import type { ConfigVersionDiffDto } from '@/shared/types/configComparison';

vi.mock('@/shared/api/configComparison');

const versions = [
  { versionNumber: 1, label: 'v1 (Published 2026-07-01)' },
  { versionNumber: 2, label: 'v2 (Draft)' },
];

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe('ConfigComparePanel', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows prompt when no versions selected', () => {
    render(
      <ConfigComparePanel templateId="t1" availableVersions={versions} onClose={vi.fn()} />,
      { wrapper },
    );
    expect(screen.getByText(/select two different/i)).toBeInTheDocument();
  });

  it('shows "no differences" for identical result', async () => {
    const diff: ConfigVersionDiffDto = {
      templateId: 't1', v1: 1, v2: 2, v1Label: 'v1', v2Label: 'v2',
      hasChanges: false,
      entries: [{ key: 'host', changeType: 'Unchanged', oldValue: 's1', newValue: 's1' }],
    };
    vi.mocked(api.getConfigVersionDiff).mockResolvedValue(diff);
    render(
      <ConfigComparePanel templateId="t1" availableVersions={versions} onClose={vi.fn()} />,
      { wrapper },
    );
    fireEvent.change(screen.getAllByRole('combobox')[0], { target: { value: '1' } });
    fireEvent.change(screen.getAllByRole('combobox')[1], { target: { value: '2' } });
    expect(await screen.findByText('No differences')).toBeInTheDocument();
  });

  it('renders diff table rows', async () => {
    const diff: ConfigVersionDiffDto = {
      templateId: 't1', v1: 1, v2: 2, v1Label: 'v1', v2Label: 'v2',
      hasChanges: true,
      entries: [
        { key: 'database.host', changeType: 'Changed', oldValue: 's1', newValue: 's2' },
      ],
    };
    vi.mocked(api.getConfigVersionDiff).mockResolvedValue(diff);
    render(
      <ConfigComparePanel templateId="t1" availableVersions={versions} onClose={vi.fn()} />,
      { wrapper },
    );
    fireEvent.change(screen.getAllByRole('combobox')[0], { target: { value: '1' } });
    fireEvent.change(screen.getAllByRole('combobox')[1], { target: { value: '2' } });
    expect(await screen.findByText('database.host')).toBeInTheDocument();
    expect(await screen.findByText('s1')).toBeInTheDocument();
  });

  it('shows unchanged toggle when unchanged entries exist', async () => {
    const diff: ConfigVersionDiffDto = {
      templateId: 't1', v1: 1, v2: 2, v1Label: 'v1', v2Label: 'v2',
      hasChanges: true,
      entries: [
        { key: 'host', changeType: 'Changed', oldValue: 's1', newValue: 's2' },
        { key: 'port', changeType: 'Unchanged', oldValue: '5432', newValue: '5432' },
      ],
    };
    vi.mocked(api.getConfigVersionDiff).mockResolvedValue(diff);
    render(
      <ConfigComparePanel templateId="t1" availableVersions={versions} onClose={vi.fn()} />,
      { wrapper },
    );
    fireEvent.change(screen.getAllByRole('combobox')[0], { target: { value: '1' } });
    fireEvent.change(screen.getAllByRole('combobox')[1], { target: { value: '2' } });
    expect(await screen.findByText(/show 1 unchanged/i)).toBeInTheDocument();
  });

  it('calls onClose when X button clicked', () => {
    const onClose = vi.fn();
    render(
      <ConfigComparePanel templateId="t1" availableVersions={versions} onClose={onClose} />,
      { wrapper },
    );
    fireEvent.click(screen.getByRole('button', { name: /close/i }));
    expect(onClose).toHaveBeenCalledOnce();
  });
});
```

- [ ] **Step 18: Wire `ConfigComparePanel` into `TemplatesPage`**

Open `src/MSOSync.Frontend/src/features/operations/configuration/TemplatesPage.tsx`. This requires locating where the template list is rendered and where version history is shown. Add state for the compare panel:

```tsx
// At the top of TemplatesPage (add imports):
import { ConfigComparePanel } from './components/ConfigComparePanel';

// Add state inside the component:
const [compareTemplateId, setCompareTemplateId] = useState<string | null>(null);
const [compareVersions, setCompareVersions] = useState<{ versionNumber: number; label: string }[]>([]);

// When version history is available for a template, add a "Compare versions" button:
// (Find where version history list is rendered and add):
<button
  className="text-xs text-muted-foreground hover:text-foreground underline"
  onClick={() => {
    setCompareTemplateId(template.id);
    setCompareVersions(
      (template.versions ?? []).map(v => ({
        versionNumber: v.versionNumber,
        label: v.isDraft
          ? `v${v.versionNumber} (Draft)`
          : `v${v.versionNumber} (Published ${v.publishedAt ? new Date(v.publishedAt).toLocaleDateString() : ''})`,
      }))
    );
  }}
>
  Compare versions
</button>

// At the bottom of the JSX, before closing tag, render the panel:
{compareTemplateId && (
  <ConfigComparePanel
    templateId={compareTemplateId}
    availableVersions={compareVersions}
    onClose={() => setCompareTemplateId(null)}
  />
)}
```

> **Note:** The exact location depends on the current TemplatesPage structure. Find the version history section and insert the button there. If TemplatesPage doesn't show version history, add the "Compare versions" button to the template row's action menu instead, and pass the `template.id` + any available version data.

- [ ] **Step 19: Build frontend**

```
cd src/MSOSync.Frontend && npm run build
```

Expected: 0 TypeScript errors.

- [ ] **Step 20: Run frontend tests**

```
cd src/MSOSync.Frontend && npm test -- ConfigComparePanel
```

Expected: 5 tests PASS.

- [ ] **Step 21: Run full test suite**

```
dotnet test tests/MSOSync.MetadataTests -v normal
```

Expected: all MetadataTests PASS (JsonDiffEngine + ConfigurationComparisonService tests included).

- [ ] **Step 22: Commit**

```
git add src/MSOSync.Metadata/Configuration/JsonDiffEngine.cs
git add src/MSOSync.Metadata/Configuration/Dtos/ConfigVersionDiffDto.cs
git add src/MSOSync.Metadata/Configuration/IConfigurationComparisonService.cs
git add src/MSOSync.Metadata/Configuration/ConfigurationComparisonService.cs
git add src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add src/MSOSync.Api/Controllers/ConfigurationTemplateController.cs
git add tests/MSOSync.MetadataTests/Configuration/JsonDiffEngineTests.cs
git add tests/MSOSync.MetadataTests/Configuration/ConfigurationComparisonServiceTests.cs
git add src/MSOSync.Frontend/src/shared/types/configComparison.ts
git add src/MSOSync.Frontend/src/shared/api/configComparison.ts
git add src/MSOSync.Frontend/src/shared/hooks/useConfigComparison.ts
git add src/MSOSync.Frontend/src/features/operations/configuration/components/ConfigComparePanel.tsx
git add src/MSOSync.Frontend/src/features/operations/configuration/components/__tests__/ConfigComparePanel.test.tsx
git add src/MSOSync.Frontend/src/features/operations/configuration/TemplatesPage.tsx
git commit -m "feat(2B.3-T2): Configuration Comparison — JsonDiffEngine, compare endpoint, ConfigComparePanel"
```
